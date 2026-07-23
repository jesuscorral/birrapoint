using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Evaluations;

/// <summary>
/// T080: HTTP-level contract tests for FR-031's discrepancy consensus alert — divergent submission
/// → `PendingConsensus` + `DiscrepancyAlert`, the ≥3-judge "only the outliers are involved" edge
/// case, `PUT /me/tables/{tableId}/evaluations/{evaluationId}` gated to involved judges only
/// (Clarification Q2, `409 evaluation-locked` otherwise), and that a table can't close while a
/// discrepancy is open (FR-032, `409 discrepancy-open` — the gate already lives in CloseTable.cs;
/// this proves the new detection code actually populates the DiscrepancyAlert it depends on).
/// </summary>
public sealed class DiscrepancyApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string StyleCodeApa = "21A";
    private const string LongComment = "This comment is long enough to satisfy the minimum length rule.";

    private HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    /// <summary>Same email convention as SubmitEvaluationApiTests/CloseTableApiTests —
    /// GetJudgeRecordsAsync resolves Judge rows by email, not `sub`.</summary>
    private HttpClient JudgeClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, $"{sub}@brew.example", "JUDGE"));
        return client;
    }

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Discrepancy")
    {
        var response = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            name = $"{namePrefix} {Guid.NewGuid():N}",
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        });
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> TransitionStateAsync(HttpClient client, Guid competitionId, string target) =>
        client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target });

    private static string NewBlindCode() => $"B{Guid.NewGuid():N}"[..8];

    private async Task<Guid> SeedParticipantAsync(Guid competitionId, string name, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var participant = new Participant { CompetitionId = competitionId, Name = name, Email = email };
        db.Participants.Add(participant);
        await db.SaveChangesAsync();
        return participant.Id;
    }

    private async Task<Guid> SeedBeerEntryAsync(
        Guid competitionId, Guid participantId, string beerName, string styleCode = StyleCodeApa)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = new BeerEntry
        {
            CompetitionId = competitionId,
            ParticipantId = participantId,
            BeerName = beerName,
            StyleCode = styleCode,
            BlindCode = NewBlindCode(),
        };
        db.BeerEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<Guid> SeedJudgeAsync(Guid competitionId, string email, string keycloakUserId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judge = new Judge
        {
            CompetitionId = competitionId,
            Email = email,
            DisplayName = email.Split('@')[0],
            KeycloakUserId = keycloakUserId,
        };
        db.Judges.Add(judge);
        await db.SaveChangesAsync();
        return judge.Id;
    }

    private async Task<Guid> SeedTableAsync(Guid competitionId, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var table = new TastingTable { CompetitionId = competitionId, Name = name };
        db.TastingTables.Add(table);
        await db.SaveChangesAsync();
        return table.Id;
    }

    private async Task SeedTableJudgeAsync(Guid tableId, Guid judgeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableJudges.Add(new TableJudge { TastingTableId = tableId, JudgeId = judgeId });
        await db.SaveChangesAsync();
    }

    private async Task SeedTableSampleAsync(Guid tableId, Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableSamples.Add(new TableSample { TastingTableId = tableId, BeerEntryId = beerEntryId });
        await db.SaveChangesAsync();
    }

    /// <summary>Mirrors what POST .../order (FixOrder) would do, without the extra HTTP round-trip
    /// (same convention as SubmitEvaluationApiTests/CloseTableApiTests).</summary>
    private async Task FixOrderDirectlyAsync(Guid tableId, Guid judgeId, IReadOnlyList<Guid> orderedEntryIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var table = await db.TastingTables.SingleAsync(t => t.Id == tableId);
        table.OrderFixedByJudgeId = judgeId;
        table.OrderFixedAt = DateTimeOffset.UtcNow;

        var samples = await db.TableSamples.Where(ts => ts.TastingTableId == tableId).ToListAsync();
        for (var i = 0; i < orderedEntryIds.Count; i++)
        {
            samples.Single(s => s.BeerEntryId == orderedEntryIds[i]).SequenceOrder = i + 1;
        }

        await db.SaveChangesAsync();
    }

    private async Task<int> CountOpenAlertsAsync(Guid tableId, Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.DiscrepancyAlerts.CountAsync(
            a => a.TastingTableId == tableId && a.BeerEntryId == beerEntryId && a.Status == DiscrepancyStatus.Open);
    }

    private async Task<List<EvaluationStatus>> GetEvaluationStatusesAsync(Guid tableId, Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Evaluations
            .Where(e => e.TastingTableId == tableId && e.BeerEntryId == beerEntryId)
            .Select(e => e.Status)
            .ToListAsync();
    }

    private static object Scores(int aroma, int appearance, int flavor, int mouthfeel, int overall) =>
        new { aroma, appearance, flavor, mouthfeel, overall };

    private static object ValidComments() => new
    {
        aroma = LongComment,
        appearance = LongComment,
        flavor = LongComment,
        mouthfeel = LongComment,
        overall = LongComment,
    };

    private static Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, Guid tableId, Guid beerEntryId, object scores, object? comments = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/me/tables/{tableId}/evaluations")
        {
            Content = JsonContent.Create(new { beerEntryId, scores, comments = comments ?? ValidComments() }),
        };
        request.Headers.Add("X-Idempotency-Key", $"comp:{tableId}:judge:{beerEntryId}");
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> AdjustAsync(
        HttpClient client, Guid tableId, Guid evaluationId, object scores, object? comments = null) =>
        client.PutAsJsonAsync(
            $"/api/v1/me/tables/{tableId}/evaluations/{evaluationId}",
            new { scores, comments = comments ?? ValidComments() });

    private static Task<HttpResponseMessage> GetDiscrepanciesAsync(HttpClient client, Guid tableId) =>
        client.GetAsync($"/api/v1/me/tables/{tableId}/discrepancies");

    private static Task<HttpResponseMessage> CloseAsync(HttpClient client, Guid tableId) =>
        client.PostAsync($"/api/v1/me/tables/{tableId}/close", content: null);

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
    public async Task Two_judges_more_than_7_apart_are_both_held_PendingConsensus_with_a_discrepancy()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);

        using var judgeA = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeB = JudgeClient(fixture.Judges[1].JudgeSub);

        var first = await SubmitAsync(judgeA, fixture.TableId, fixture.EntryId, Scores(10, 2, 16, 4, 8)); // total 40
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var firstDocument = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        // Nothing to compare against yet — a lone submission is never a discrepancy.
        Assert.Equal("Confirmed", firstDocument.RootElement.GetProperty("status").GetString());
        Assert.False(firstDocument.RootElement.TryGetProperty("discrepancy", out var noDiscrepancy) && noDiscrepancy.ValueKind != JsonValueKind.Null);

        var second = await SubmitAsync(judgeB, fixture.TableId, fixture.EntryId, Scores(5, 1, 10, 2, 2)); // total 20, diff 20
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var secondDocument = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
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
        using var discrepanciesDocument = JsonDocument.Parse(await discrepanciesForA.Content.ReadAsStringAsync());
        Assert.Single(discrepanciesDocument.RootElement.EnumerateArray());
    }

    // ---- ≥3 judges: only the pair(s) more than 7 apart are involved (spec edge case) --------------

    [Fact]
    public async Task Three_judges_only_the_outlier_pair_more_than_7_apart_are_involved()
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
        using var thirdDocument = JsonDocument.Parse(await third.Content.ReadAsStringAsync());
        var thirdRoot = thirdDocument.RootElement;
        // Judge C's own total is within 7 of both others, so their own evaluation is Confirmed, and
        // their own response's `discrepancy` field reflects THEIR situation (null) — even though an
        // alert remains open for A/B on this same entry (GET .../discrepancies is what surfaces that
        // to a caller who's actually involved, asserted separately below).
        Assert.Equal("Confirmed", thirdRoot.GetProperty("status").GetString());
        Assert.False(thirdRoot.TryGetProperty("discrepancy", out var thirdDiscrepancy) && thirdDiscrepancy.ValueKind != JsonValueKind.Null);

        var discrepanciesForA = await GetDiscrepanciesAsync(judgeA, fixture.TableId);
        using var forA = JsonDocument.Parse(await discrepanciesForA.Content.ReadAsStringAsync());
        Assert.Single(forA.RootElement.EnumerateArray());

        var discrepanciesForC = await GetDiscrepanciesAsync(judgeC, fixture.TableId);
        using var forC = JsonDocument.Parse(await discrepanciesForC.Content.ReadAsStringAsync());
        // Judge C is never involved, so no alert is returned for them specifically.
        Assert.Empty(forC.RootElement.EnumerateArray());
    }

    // ---- PUT adjustment: only an involved judge, only while the alert is open (Clarification Q2) --

    [Fact]
    public async Task Adjustment_by_an_involved_judge_that_converges_everyone_resolves_the_discrepancy()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);

        using var judgeA = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeB = JudgeClient(fixture.Judges[1].JudgeSub);

        var first = await SubmitAsync(judgeA, fixture.TableId, fixture.EntryId, Scores(10, 2, 16, 4, 8)); // 40
        var evaluationIdA = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("evaluationId").GetGuid();

        var second = await SubmitAsync(judgeB, fixture.TableId, fixture.EntryId, Scores(5, 1, 10, 2, 2)); // 20, diff 20 -> both involved
        Assert.Equal("PendingConsensus", (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        var adjustment = await AdjustAsync(judgeA, fixture.TableId, evaluationIdA, Scores(6, 1, 12, 3, 3)); // 25, diff to 20 is 5

        Assert.Equal(HttpStatusCode.OK, adjustment.StatusCode);
        using var adjustedDocument = JsonDocument.Parse(await adjustment.Content.ReadAsStringAsync());
        var adjustedRoot = adjustedDocument.RootElement;
        Assert.Equal("Confirmed", adjustedRoot.GetProperty("status").GetString());
        Assert.Equal(25, adjustedRoot.GetProperty("total").GetInt32());
        Assert.False(adjustedRoot.TryGetProperty("discrepancy", out var discrepancyAfter) && discrepancyAfter.ValueKind != JsonValueKind.Null);

        Assert.Equal(0, await CountOpenAlertsAsync(fixture.TableId, fixture.EntryId));

        var remainingDiscrepancies = await GetDiscrepanciesAsync(judgeB, fixture.TableId);
        using var remaining = JsonDocument.Parse(await remainingDiscrepancies.Content.ReadAsStringAsync());
        Assert.Empty(remaining.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Adjustment_of_an_evaluation_with_no_open_discrepancy_returns_409_evaluation_locked()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 1);
        using var judge = JudgeClient(fixture.Judges[0].JudgeSub);

        var submit = await SubmitAsync(judge, fixture.TableId, fixture.EntryId, Scores(10, 2, 16, 4, 8));
        var evaluationId = (await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("evaluationId").GetGuid();

        var response = await AdjustAsync(judge, fixture.TableId, evaluationId, Scores(9, 2, 15, 4, 8));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("evaluation-locked", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Adjustment_of_someone_elses_evaluation_returns_404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);

        using var judgeA = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeB = JudgeClient(fixture.Judges[1].JudgeSub);

        var first = await SubmitAsync(judgeA, fixture.TableId, fixture.EntryId, Scores(10, 2, 16, 4, 8)); // 40
        var evaluationIdA = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("evaluationId").GetGuid();
        await SubmitAsync(judgeB, fixture.TableId, fixture.EntryId, Scores(5, 1, 10, 2, 2)); // 20, opens the alert

        var response = await AdjustAsync(judgeB, fixture.TableId, evaluationIdA, Scores(9, 2, 15, 4, 8));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- FR-032: table close is blocked while a discrepancy is open, succeeds once resolved -------

    [Fact]
    public async Task Close_is_blocked_by_an_open_discrepancy_and_succeeds_once_it_is_resolved()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);

        using var judgeA = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeB = JudgeClient(fixture.Judges[1].JudgeSub);

        var first = await SubmitAsync(judgeA, fixture.TableId, fixture.EntryId, Scores(10, 2, 16, 4, 8)); // 40
        var evaluationIdA = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("evaluationId").GetGuid();
        await SubmitAsync(judgeB, fixture.TableId, fixture.EntryId, Scores(5, 1, 10, 2, 2)); // 20, diff 20 -> opens the alert

        var blockedClose = await CloseAsync(judgeA, fixture.TableId);

        Assert.Equal(HttpStatusCode.Conflict, blockedClose.StatusCode);
        using var blockedDocument = JsonDocument.Parse(await blockedClose.Content.ReadAsStringAsync());
        Assert.Contains("discrepancy-open", blockedDocument.RootElement.GetProperty("type").GetString());

        var resolve = await AdjustAsync(judgeA, fixture.TableId, evaluationIdA, Scores(6, 1, 12, 3, 3)); // 25, diff to 20 is 5
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);

        var close = await CloseAsync(judgeA, fixture.TableId);

        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
    }

    // ---- Concurrency race on the alert insert itself, not just the evaluation insert -------------

    [Fact]
    public async Task Two_different_judges_submitting_divergent_totals_concurrently_leave_exactly_one_open_alert()
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

        var taskA = SubmitAsync(judgeA, fixture.TableId, fixture.EntryId, Scores(10, 2, 16, 4, 8)); // 40
        var taskB = SubmitAsync(judgeB, fixture.TableId, fixture.EntryId, Scores(5, 1, 10, 2, 2)); // 20, diff 20 -> both involved
        var responses = await Task.WhenAll(taskA, taskB);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        Assert.Equal(1, await CountOpenAlertsAsync(fixture.TableId, fixture.EntryId));

        var statuses = await GetEvaluationStatusesAsync(fixture.TableId, fixture.EntryId);
        Assert.Equal(2, statuses.Count);
        Assert.All(statuses, s => Assert.Equal(EvaluationStatus.PendingConsensus, s));
    }
}
