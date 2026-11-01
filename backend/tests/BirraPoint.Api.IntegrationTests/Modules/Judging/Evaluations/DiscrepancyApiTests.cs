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

namespace BirraPoint.Api.IntegrationTests.Modules.Judging.Evaluations;

/// <summary>
/// HTTP-level contract tests for FR-031's discrepancy consensus alert — divergent submission
/// → `PendingConsensus` + `DiscrepancyAlert`, the ≥3-judge "only the outliers are involved" edge
/// case, that a table can't close while a discrepancy is open (FR-032, `409 discrepancy-open` — the
/// gate already lives in Modules/Judging/Features/Evaluations/Features/CloseTable/CloseTableCommand.cs; this proves the
/// new detection code actually populates the DiscrepancyAlert it depends on), and the alert-insert
/// race. The per-judge adjustment endpoint is covered by AdjustEvaluationApiTests.
/// </summary>
public sealed class DiscrepancyApiTests(ApiFactory factory) : EvaluationsApiTestBase(factory), IClassFixture<ApiFactory>
{
    private async Task<List<EvaluationStatus>> GetEvaluationStatusesAsync(Guid tableId, Guid beerEntryId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Evaluations
            .Where(e => e.TastingTableId == tableId && e.BeerEntryId == beerEntryId)
            .Select(e => e.Status)
            .ToListAsync();
    }

    private static async Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, Guid tableId, Guid beerEntryId, object scores, object? comments = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/me/tables/{tableId}/evaluations")
        {
            Content = JsonContent.Create(new { beerEntryId, scores, comments = comments ?? ValidComments() }),
        };
        request.Headers.Add("X-Idempotency-Key", $"comp:{tableId}:judge:{beerEntryId}");
        return await client.SendAsync(request);
    }

    /// <summary>Seeds a competition, one table with a single sample, and <paramref name="judgeCount"/>
    /// actively-assigned judges — ready for submission (competition InEvaluation, order fixed by the
    /// first seeded judge). A single shared sample keeps the "next in sequence" gate trivially
    /// satisfied for every judge (FR-022 is not what this suite is about).</summary>
    private async Task<(Guid CompetitionId, Guid TableId, List<(Guid JudgeId, string JudgeSub)> Judges, Guid EntryId)>
        SeedReadyTableAsync(HttpClient organizer, int judgeCount)
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

        var participantId = await SeedParticipantAsync(competitionId, "Brewer", $"brewer-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Secret Beer");
        await SeedTableSampleAsync(tableId, entryId);

        await TransitionStateAsync(organizer, competitionId, "Active");
        await TransitionStateAsync(organizer, competitionId, "InEvaluation");
        await FixOrderDirectlyAsync(tableId, judges[0].JudgeId, [entryId]);

        return (competitionId, tableId, judges, entryId);
    }

    // ---- Divergent submission opens a discrepancy (FR-031) ----------------------------------------

    [Fact]
    public async Task TwoJudgesMoreThan7ApartAreBothHeldPendingConsensusWithADiscrepancy()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);

        using var judgeA = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeB = JudgeClient(fixture.Judges[1].JudgeSub);

        var first = await SubmitAsync(judgeA, fixture.TableId, fixture.EntryId, Scores(10, 2, 16, 4, 8)); // total 40
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var firstDocument = JsonDocument.Parse(await first.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        // Nothing to compare against yet — a lone submission is never a discrepancy.
        Assert.Equal("Confirmed", firstDocument.RootElement.GetProperty("status").GetString());
        Assert.False(firstDocument.RootElement.TryGetProperty("discrepancy", out var noDiscrepancy) && noDiscrepancy.ValueKind != JsonValueKind.Null);

        var second = await SubmitAsync(judgeB, fixture.TableId, fixture.EntryId, Scores(5, 1, 10, 2, 2)); // total 20, diff 20
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var secondDocument = JsonDocument.Parse(await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var secondRoot = secondDocument.RootElement;

        Assert.Equal("PendingConsensus", secondRoot.GetProperty("status").GetString());
        var discrepancy = secondRoot.GetProperty("discrepancy");
        Assert.Equal(JsonValueKind.String, discrepancy.GetProperty("blindCode").ValueKind);
        var totals = discrepancy.GetProperty("totals").EnumerateArray().ToList();
        Assert.Equal(2, totals.Count);
        Assert.Contains(totals, t => t.GetProperty("total").GetInt32() == 40);
        Assert.Contains(totals, t => t.GetProperty("total").GetInt32() == 20 && t.GetProperty("isMine").GetBoolean());
        Assert.All(totals, t => Assert.Equal(JsonValueKind.String, t.GetProperty("evaluationId").ValueKind));

        Assert.Equal(1, await CountOpenAlertsAsync(fixture.TableId, fixture.EntryId));

        // Judge A didn't trigger the reconciliation but is still involved — GET .../discrepancies
        // must reflect the alert for them too.
        var discrepanciesForA = await GetDiscrepanciesAsync(judgeA, fixture.TableId);
        Assert.Equal(HttpStatusCode.OK, discrepanciesForA.StatusCode);
        using var discrepanciesDocument = JsonDocument.Parse(await discrepanciesForA.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Single(discrepanciesDocument.RootElement.EnumerateArray());
    }

    // ---- ≥3 judges: only the pair(s) more than 7 apart are involved (spec edge case) --------------

    [Fact]
    public async Task ThreeJudgesOnlyTheOutlierPairMoreThan7ApartAreInvolved()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 3);

        using var judgeA = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeB = JudgeClient(fixture.Judges[1].JudgeSub);
        using var judgeC = JudgeClient(fixture.Judges[2].JudgeSub);

        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeA, fixture.TableId, fixture.EntryId, Scores(10, 2, 16, 4, 8))).StatusCode); // 40
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeB, fixture.TableId, fixture.EntryId, Scores(12, 3, 18, 5, 10))).StatusCode); // 48, diff(40,48)=8 -> both involved

        var third = await SubmitAsync(judgeC, fixture.TableId, fixture.EntryId, Scores(11, 3, 17, 4, 9)); // 44: diff to 40 is 4, diff to 48 is 4
        Assert.Equal(HttpStatusCode.Created, third.StatusCode);
        using var thirdDocument = JsonDocument.Parse(await third.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var thirdRoot = thirdDocument.RootElement;
        // Judge C's own total is within 7 of both others, so their own evaluation is Confirmed, and
        // their own response's `discrepancy` field reflects THEIR situation (null) — even though an
        // alert remains open for A/B on this same entry (GET .../discrepancies is what surfaces that
        // to a caller who's actually involved, asserted separately below).
        Assert.Equal("Confirmed", thirdRoot.GetProperty("status").GetString());
        Assert.False(thirdRoot.TryGetProperty("discrepancy", out var thirdDiscrepancy) && thirdDiscrepancy.ValueKind != JsonValueKind.Null);

        var discrepanciesForA = await GetDiscrepanciesAsync(judgeA, fixture.TableId);
        using var forA = JsonDocument.Parse(await discrepanciesForA.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Single(forA.RootElement.EnumerateArray());

        var discrepanciesForC = await GetDiscrepanciesAsync(judgeC, fixture.TableId);
        using var forC = JsonDocument.Parse(await discrepanciesForC.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        // Judge C is never involved, so no alert is returned for them specifically.
        Assert.Empty(forC.RootElement.EnumerateArray());
    }

    // ---- FR-032: table close is blocked while a discrepancy is open, succeeds once resolved -------

    [Fact]
    public async Task CloseIsBlockedByAnOpenDiscrepancyAndSucceedsOnceItIsResolved()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);

        using var judgeA = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeB = JudgeClient(fixture.Judges[1].JudgeSub);

        var first = await SubmitAsync(judgeA, fixture.TableId, fixture.EntryId, Scores(10, 2, 16, 4, 8)); // 40
        var evaluationIdA = (await first.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("evaluationId").GetGuid();
        await SubmitAsync(judgeB, fixture.TableId, fixture.EntryId, Scores(5, 1, 10, 2, 2)); // 20, diff 20 -> opens the alert

        var blockedClose = await CloseAsync(judgeA, fixture.TableId);

        Assert.Equal(HttpStatusCode.Conflict, blockedClose.StatusCode);
        using var blockedDocument = JsonDocument.Parse(await blockedClose.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("discrepancy-open", blockedDocument.RootElement.GetProperty("type").GetString(), StringComparison.Ordinal);

        var resolve = await AdjustAsync(judgeA, fixture.TableId, evaluationIdA, Scores(6, 1, 12, 3, 3)); // 25, diff to 20 is 5
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);

        var close = await CloseAsync(judgeA, fixture.TableId);

        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
    }

    // ---- Concurrency race on the alert insert itself, not just the evaluation insert -------------

    [Fact]
    public async Task TwoDifferentJudgesSubmittingDivergentTotalsConcurrentlyLeaveExactlyOneOpenAlert()
    {
        // Regression test for a real race PR review caught: two DIFFERENT judges (so the
        // evaluation's own (JudgeId, BeerEntryId) unique index never engages — SubmitEvaluationApiTests'
        // existing race test only covers the SAME judge racing itself) can both reach
        // DiscrepancyReconciler seeing no existing Open alert and both try to insert one, hitting
        // DiscrepancyAlertConfiguration's partial unique index. Before the fix this surfaced as an
        // unhandled 500 for whichever request lost the race; DiscrepancyReconciler.ReconcileAndSaveAsync
        // now retries against what the winner actually persisted.
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);

        using var judgeA = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeB = JudgeClient(fixture.Judges[1].JudgeSub);

        var responses = await Task.WhenAll(
            SubmitAsync(judgeA, fixture.TableId, fixture.EntryId, Scores(10, 2, 16, 4, 8)), // 40
            SubmitAsync(judgeB, fixture.TableId, fixture.EntryId, Scores(5, 1, 10, 2, 2))); // 20, diff 20 -> both involved

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        Assert.Equal(1, await CountOpenAlertsAsync(fixture.TableId, fixture.EntryId));

        var statuses = await GetEvaluationStatusesAsync(fixture.TableId, fixture.EntryId);
        Assert.Equal(2, statuses.Count);
        Assert.All(statuses, s => Assert.Equal(EvaluationStatus.PendingConsensus, s));
    }
}
