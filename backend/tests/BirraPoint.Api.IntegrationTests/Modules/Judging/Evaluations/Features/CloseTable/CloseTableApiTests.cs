using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Judging.Evaluations.Features.CloseTable;

/// <summary>
/// HTTP-level contract tests for POST /me/tables/{tableId}/close (contracts/rest-api.md
/// §Judge workspace, FR-033/FR-042) — the completeness precondition (→ 409 evaluations-incomplete),
/// the double-close guard (→ 409 table-closed), that closing actually flips TastingTable.State so the
/// *existing* SubmitEvaluation table-closed gate engages (FR-034), and the correctly-averaged
/// consolidated mean (FR-042). The organizer post-close correction lives in CorrectEvaluationApiTests.
/// </summary>
public sealed class CloseTableApiTests(ApiFactory factory) : EvaluationsApiTestBase(factory), IClassFixture<ApiFactory>
{
    private async Task<string> GetBlindCodeAsync(Guid beerEntryId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.BeerEntries.Where(be => be.Id == beerEntryId).Select(be => be.BlindCode).SingleAsync();
    }

    private async Task<(TableState State, DateTimeOffset? ClosedAt)> GetTableStateAsync(Guid tableId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var table = await db.TastingTables.SingleAsync(t => t.Id == tableId);
        return (table.State, table.ClosedAt);
    }

    private static async Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, Guid tableId, Guid beerEntryId, object? scores = null, object? comments = null, object? descriptors = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/me/tables/{tableId}/evaluations")
        {
            Content = JsonContent.Create(new
            {
                beerEntryId,
                scores = scores ?? ValidScores(),
                comments = comments ?? ValidComments(),
                descriptors,
            }),
        };
        request.Headers.Add("X-Idempotency-Key", $"comp:{tableId}:judge:{beerEntryId}");
        return await client.SendAsync(request);
    }

    /// <summary>Seeds a competition, one table with <paramref name="sampleCount"/> samples, and
    /// <paramref name="judgeCount"/> actively-assigned judges. Competition state and order-fixed-ness
    /// are left to the caller.</summary>
    private async Task<(Guid CompetitionId, Guid TableId, List<(Guid JudgeId, string JudgeSub)> Judges, List<Guid> EntryIds)>
        SeedTableWithSamplesAsync(HttpClient organizer, int sampleCount = 1, int judgeCount = 1)
    {
        var competitionId = await CreateCompetitionAsync(organizer);
        var tableId = await SeedTableAsync(competitionId, $"Table {Guid.NewGuid():N}");

        var judges = new List<(Guid JudgeId, string JudgeSub)>();
        for (var j = 0; j < judgeCount; j++)
        {
            var judgeSub = $"judge-{Guid.NewGuid():N}";
            var judgeId = await SeedJudgeAsync(competitionId, $"{judgeSub}@brew.example", judgeSub);
            await SeedTableJudgeAsync(tableId, judgeId);
            judges.Add((judgeId, judgeSub));
        }

        var entryIds = new List<Guid>();
        for (var i = 0; i < sampleCount; i++)
        {
            var participantId = await SeedParticipantAsync(
                competitionId, $"Brewer {i}", $"brewer-{Guid.NewGuid():N}@brew.example");
            var entryId = await SeedBeerEntryAsync(competitionId, participantId, $"Secret Beer {i}");
            await SeedTableSampleAsync(tableId, entryId);
            entryIds.Add(entryId);
        }

        return (competitionId, tableId, judges, entryIds);
    }

    /// <summary>Fixture fully ready for a successful submission: competition InEvaluation, order
    /// fixed by the first seeded judge.</summary>
    private async Task<(Guid CompetitionId, Guid TableId, List<(Guid JudgeId, string JudgeSub)> Judges, List<Guid> EntryIds)>
        SeedReadyTableAsync(HttpClient organizer, int sampleCount = 1, int judgeCount = 1)
    {
        var fixture = await SeedTableWithSamplesAsync(organizer, sampleCount, judgeCount);
        await TransitionStateAsync(organizer, fixture.CompetitionId, "Active");
        await TransitionStateAsync(organizer, fixture.CompetitionId, "InEvaluation");
        await FixOrderDirectlyAsync(fixture.TableId, fixture.Judges[0].JudgeId, fixture.EntryIds);
        return fixture;
    }

    // ---- Completeness precondition (FR-033) -----------------------------------------------------

    [Fact]
    public async Task CloseWithIncompleteEvaluationsReturns409EvaluationsIncompleteWithMissingBlindCodes()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, sampleCount: 2, judgeCount: 1);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);

        var submitFirst = await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0]);
        Assert.Equal(HttpStatusCode.Created, submitFirst.StatusCode);
        // fixture.EntryIds[1] is never submitted.

        var missingBlindCode = await GetBlindCodeAsync(fixture.EntryIds[1]);

        var response = await CloseAsync(judgeClient, fixture.TableId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = document.RootElement;
        Assert.Contains("evaluations-incomplete", root.GetProperty("type").GetString(), StringComparison.Ordinal);
        var missing = root.GetProperty("missing").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal([missingBlindCode], missing);
    }

    // ---- Double-close guard -----------------------------------------------------------------------

    [Fact]
    public async Task CloseATableTwiceReturns409TableClosedOnTheSecondAttempt()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);

        var submit = await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0]);
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);

        var firstClose = await CloseAsync(judgeClient, fixture.TableId);
        Assert.Equal(HttpStatusCode.OK, firstClose.StatusCode);

        var secondClose = await CloseAsync(judgeClient, fixture.TableId);

        Assert.Equal(HttpStatusCode.Conflict, secondClose.StatusCode);
        using var document = JsonDocument.Parse(await secondClose.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("table-closed", document.RootElement.GetProperty("type").GetString(), StringComparison.Ordinal);
    }

    // ---- Happy path: closes successfully, judge response omits organizer-only data (FR-042) -------

    [Fact]
    public async Task CloseSucceedsWhenCompleteAndTheJudgeResponseOmitsConsolidatedScores()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, sampleCount: 1, judgeCount: 2);
        var entryId = fixture.EntryIds[0];

        using var judgeAClient = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeBClient = JudgeClient(fixture.Judges[1].JudgeSub);

        var scoresA = new { aroma = 10, appearance = 2, flavor = 15, mouthfeel = 4, overall = 8 }; // total 39
        var scoresB = new { aroma = 11, appearance = 3, flavor = 17, mouthfeel = 4, overall = 9 }; // total 44, within 7 of scoresA (FR-031) so no discrepancy blocks the close below

        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeAClient, fixture.TableId, entryId, scoresA)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeBClient, fixture.TableId, entryId, scoresB)).StatusCode);

        var closeResponse = await CloseAsync(judgeAClient, fixture.TableId);

        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        using var document = JsonDocument.Parse(await closeResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        // consolidatedScores is organizer-only (contracts/rest-api.md, signalr-hub.md): the closing
        // judge's own HTTP response must not carry per-sample means, only confirmation. The mean
        // computation itself (CloseTableRules.ComputeMean) is covered directly by
        // CloseTableTests.cs's unit tests and, end-to-end via a real persisted evaluation, by
        // CorrectEvaluationApiTests.OrganizerCorrectionAfterCloseSucceedsRecomputesTotalAndMeanAndIsAudited.
        Assert.Equal(fixture.TableId, document.RootElement.GetProperty("tableId").GetGuid());
        Assert.False(document.RootElement.TryGetProperty("consolidatedScores", out _));

        var (state, closedAt) = await GetTableStateAsync(fixture.TableId);
        Assert.Equal(TableState.Closed, state);
        Assert.NotNull(closedAt);
    }

    // ---- Post-close immutability: the existing SubmitEvaluation gate now engages (FR-034) --------

    [Fact]
    public async Task PostCloseEvaluationSubmissionByAJudgeSeededAfterCloseReturns409TableClosed()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);

        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await CloseAsync(judgeClient, fixture.TableId)).StatusCode);

        // A second judge, seeded (and assigned to the table) only after close — proves the
        // *existing* SubmitEvaluation table-state gate is what engages once CloseTable
        // flips TastingTable.State, not merely that direct test seeding of a closed table works.
        var latecomerSub = $"judge-{Guid.NewGuid():N}";
        var latecomerId = await SeedJudgeAsync(fixture.CompetitionId, $"{latecomerSub}@brew.example", latecomerSub);
        await SeedTableJudgeAsync(fixture.TableId, latecomerId);
        using var latecomerClient = JudgeClient(latecomerSub);

        var lateSubmit = await SubmitAsync(latecomerClient, fixture.TableId, fixture.EntryIds[0]);

        Assert.Equal(HttpStatusCode.Conflict, lateSubmit.StatusCode);
        using var document = JsonDocument.Parse(await lateSubmit.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("table-closed", document.RootElement.GetProperty("type").GetString(), StringComparison.Ordinal);
    }
}
