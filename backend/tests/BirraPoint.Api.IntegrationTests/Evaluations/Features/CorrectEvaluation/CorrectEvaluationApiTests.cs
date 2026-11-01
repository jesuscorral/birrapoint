using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Evaluations.Features.CorrectEvaluation;

/// <summary>
/// HTTP-level contract tests for PUT /competitions/{id}/evaluations/{evaluationId}
/// (contracts/rest-api.md §Monitoring &amp; audit, FR-035) — the organizer post-close correction
/// (recomputed total/mean, audited, descriptors preserved, organizer-ownership-scoped 404s, same
/// boundary validation as judge submission).
/// </summary>
public sealed class CorrectEvaluationApiTests(ApiFactory factory) : EvaluationsApiTestBase(factory), IClassFixture<ApiFactory>
{
    private async Task<AuditLog> GetAuditLogAsync(Guid evaluationId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == evaluationId.ToString());
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

    private static Task<HttpResponseMessage> CorrectAsync(
        HttpClient client, Guid competitionId, Guid evaluationId, object? scores = null, object? comments = null) =>
        client.PutAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/evaluations/{evaluationId}",
            new { scores = scores ?? ValidScores(), comments = comments ?? ValidComments() });

    private async Task<string?> GetDescriptorsJsonAsync(Guid evaluationId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evaluation = await db.Evaluations.AsNoTracking().SingleAsync(e => e.Id == evaluationId);
        return evaluation.DescriptorsJson;
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

    // ---- Organizer post-close correction (FR-035) --------------------------------------------------

    [Fact]
    public async Task OrganizerCorrectionAfterCloseSucceedsRecomputesTotalAndMeanAndIsAudited()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);

        var originalScores = new { aroma = 10, appearance = 2, flavor = 15, mouthfeel = 4, overall = 8 }; // total 39
        var submit = await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0], originalScores);
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);
        var evaluationId = (await submit.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("evaluationId").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await CloseAsync(judgeClient, fixture.TableId)).StatusCode);

        var correctedScores = new { aroma = 12, appearance = 3, flavor = 20, mouthfeel = 5, overall = 10 }; // total 50
        var correctionResponse = await CorrectAsync(organizer, fixture.CompetitionId, evaluationId, correctedScores);

        Assert.Equal(HttpStatusCode.OK, correctionResponse.StatusCode);
        using var document = JsonDocument.Parse(await correctionResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(50, document.RootElement.GetProperty("total").GetInt32());
        // Only evaluation for this sample, so the consolidated mean equals the corrected total.
        Assert.Equal(50m, document.RootElement.GetProperty("consolidatedMean").GetDecimal());

        var auditLog = await GetAuditLogAsync(evaluationId);
        Assert.Equal("EvaluationCorrected", auditLog.Action);
        Assert.Equal(nameof(Evaluation), auditLog.EntityType);
        Assert.Equal(evaluationId.ToString(), auditLog.EntityId);

        using var data = JsonDocument.Parse(auditLog.DataJson);
        Assert.Equal(8, data.RootElement.GetProperty("before").GetProperty("OverallScore").GetInt32());
        Assert.Equal(10, data.RootElement.GetProperty("after").GetProperty("OverallScore").GetInt32());
    }

    [Fact]
    public async Task CorrectingOnlyTheScoresPreservesTheJudgesExistingDescriptors()
    {
        // senior-review M1: descriptors/feedback are optional on the correction request — an
        // organizer correcting just the five scores (the contract's own documented minimal body)
        // must not silently wipe the judge's tasting descriptors by omitting fields the request
        // never required.
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);

        var descriptors = new { appearance = new { color = "Golden", retention = 60 } };
        var submit = await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0], descriptors: descriptors);
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);
        var evaluationId = (await submit.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("evaluationId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await CloseAsync(judgeClient, fixture.TableId)).StatusCode);

        var descriptorsBeforeCorrection = await GetDescriptorsJsonAsync(evaluationId);
        Assert.NotNull(descriptorsBeforeCorrection);
        Assert.Contains("Golden", descriptorsBeforeCorrection, StringComparison.Ordinal);

        // The correction body omits `descriptors` entirely — exactly what CorrectAsync's own
        // default (scores/comments only) sends.
        var correctionResponse = await CorrectAsync(organizer, fixture.CompetitionId, evaluationId);
        Assert.Equal(HttpStatusCode.OK, correctionResponse.StatusCode);

        var descriptorsAfterCorrection = await GetDescriptorsJsonAsync(evaluationId);
        Assert.Equal(descriptorsBeforeCorrection, descriptorsAfterCorrection);
    }

    [Fact]
    public async Task CorrectionByANonOwningOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(owner);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);

        var submit = await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0]);
        var evaluationId = (await submit.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("evaluationId").GetGuid();
        await CloseAsync(judgeClient, fixture.TableId);

        using var otherOrganizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await CorrectAsync(otherOrganizer, fixture.CompetitionId, evaluationId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CorrectionOfANonExistentEvaluationReturns404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);

        var response = await CorrectAsync(organizer, fixture.CompetitionId, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Boundary validation reuses SubmitEvaluation's rules (400s) -------------------------------

    [Fact]
    public async Task CorrectionWithAnOutOfRangeScoreReturns400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);
        var submit = await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0]);
        var evaluationId = (await submit.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("evaluationId").GetGuid();

        var invalidScores = new { aroma = 13, appearance = 2, flavor = 15, mouthfeel = 4, overall = 8 }; // aroma cap is 12

        var response = await CorrectAsync(organizer, fixture.CompetitionId, evaluationId, invalidScores);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CorrectionWithACommentUnder20CharactersReturns400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        using var judgeClient = JudgeClient(fixture.Judges[0].JudgeSub);
        var submit = await SubmitAsync(judgeClient, fixture.TableId, fixture.EntryIds[0]);
        var evaluationId = (await submit.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("evaluationId").GetGuid();

        var shortComments = new
        {
            aroma = "too short",
            appearance = LongComment,
            flavor = LongComment,
            mouthfeel = LongComment,
            overall = LongComment,
        };

        var response = await CorrectAsync(organizer, fixture.CompetitionId, evaluationId, ValidScores(), shortComments);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
