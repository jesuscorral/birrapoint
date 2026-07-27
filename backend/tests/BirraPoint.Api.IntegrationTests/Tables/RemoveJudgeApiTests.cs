using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Tables;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Tables;

/// <summary>
/// T085: HTTP-level contract tests for DELETE /competitions/{id}/tables/{tableId}/judges/{judgeId}
/// (contracts/rest-api.md §Tables, FR-039) — live removal revokes the removed judge's access to
/// every judge-workspace slice at that table immediately (via the pre-existing
/// JudgeTableAccess.FindActiveMembershipAsync guard), is audit-logged, never leaks existence to a
/// non-owning organizer or for a judge who was never assigned/already removed, and leaves the
/// judge's already-submitted evaluations valid and readable by the organizer.
/// </summary>
public sealed class RemoveJudgeApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
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

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Removal")
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

    private async Task<AuditLog> GetAuditLogAsync(string entityId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == entityId);
    }

    private static object ValidScores() => new { aroma = 10, appearance = 2, flavor = 15, mouthfeel = 4, overall = 8 };

    private static object ValidComments() => new
    {
        aroma = LongComment,
        appearance = LongComment,
        flavor = LongComment,
        mouthfeel = LongComment,
        overall = LongComment,
    };

    private static Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, Guid tableId, Guid beerEntryId, object? scores = null, object? comments = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/me/tables/{tableId}/evaluations")
        {
            Content = JsonContent.Create(new { beerEntryId, scores = scores ?? ValidScores(), comments = comments ?? ValidComments() }),
        };
        request.Headers.Add("X-Idempotency-Key", $"comp:{tableId}:judge:{beerEntryId}");
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> GetSamplesAsync(HttpClient client, Guid tableId) =>
        client.GetAsync($"/api/v1/me/tables/{tableId}/samples");

    private static Task<HttpResponseMessage> RemoveJudgeAsync(HttpClient client, Guid competitionId, Guid tableId, Guid judgeId) =>
        client.DeleteAsync($"/api/v1/competitions/{competitionId}/tables/{tableId}/judges/{judgeId}");

    private static Task<HttpResponseMessage> GetEntryEvaluationsAsync(HttpClient client, Guid competitionId, Guid entryId) =>
        client.GetAsync($"/api/v1/competitions/{competitionId}/entries/{entryId}/evaluations");

    // ---- Discrepancy-reconciliation-on-removal helpers (mirrors DiscrepancyApiTests.cs) -----------

    private static object Scores(int aroma, int appearance, int flavor, int mouthfeel, int overall) =>
        new { aroma, appearance, flavor, mouthfeel, overall };

    private static Task<HttpResponseMessage> GetDiscrepanciesAsync(HttpClient client, Guid tableId) =>
        client.GetAsync($"/api/v1/me/tables/{tableId}/discrepancies");

    private static Task<HttpResponseMessage> CloseAsync(HttpClient client, Guid tableId) =>
        client.PostAsync($"/api/v1/me/tables/{tableId}/close", content: null);

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

    /// <summary>Seeds a competition, one table with <paramref name="sampleCount"/> samples, and
    /// <paramref name="judgeCount"/> actively-assigned judges. Competition state and order-fixed-ness
    /// are left to the caller.</summary>
    private async Task<(Guid CompetitionId, Guid TableId, List<(Guid JudgeId, string JudgeSub)> Judges, List<Guid> EntryIds)>
        SeedTableWithSamplesAsync(HttpClient organizer, int sampleCount = 1, int judgeCount = 2)
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
        SeedReadyTableAsync(HttpClient organizer, int sampleCount = 1, int judgeCount = 2)
    {
        var fixture = await SeedTableWithSamplesAsync(organizer, sampleCount, judgeCount);
        await TransitionStateAsync(organizer, fixture.CompetitionId, "Active");
        await TransitionStateAsync(organizer, fixture.CompetitionId, "InEvaluation");
        await FixOrderDirectlyAsync(fixture.TableId, fixture.Judges[0].JudgeId, fixture.EntryIds);
        return fixture;
    }

    // ---- Happy path: revokes access immediately (FR-039) -------------------------------------------

    [Fact]
    public async Task Remove_by_the_owning_organizer_returns_200_and_immediately_revokes_the_removed_judges_table_access()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        var removedJudge = fixture.Judges[0];

        var removeResponse = await RemoveJudgeAsync(organizer, fixture.CompetitionId, fixture.TableId, removedJudge.JudgeId);

        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        using var removedJudgeClient = JudgeClient(removedJudge.JudgeSub);

        var samplesResponse = await GetSamplesAsync(removedJudgeClient, fixture.TableId);
        Assert.Equal(HttpStatusCode.NotFound, samplesResponse.StatusCode);

        var submitResponse = await SubmitAsync(removedJudgeClient, fixture.TableId, fixture.EntryIds[0]);
        Assert.Equal(HttpStatusCode.NotFound, submitResponse.StatusCode);
    }

    [Fact]
    public async Task Remove_writes_an_audit_log_row_keyed_by_table_and_judge()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        var removedJudge = fixture.Judges[0];

        var removeResponse = await RemoveJudgeAsync(organizer, fixture.CompetitionId, fixture.TableId, removedJudge.JudgeId);
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        var auditLog = await GetAuditLogAsync(RemoveJudgeRules.ComputeAuditEntityId(fixture.TableId, removedJudge.JudgeId));
        Assert.Equal("JudgeRemoved", auditLog.Action);
        Assert.Equal(nameof(TableJudge), auditLog.EntityType);
    }

    // ---- Competition state gate (FR-039 scopes removal to "the live event") ------------------------

    [Fact]
    public async Task Remove_while_the_competition_is_still_Draft_returns_409_invalid_state_transition()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedTableWithSamplesAsync(organizer, judgeCount: 1);

        var response = await RemoveJudgeAsync(organizer, fixture.CompetitionId, fixture.TableId, fixture.Judges[0].JudgeId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Remove_while_the_competition_is_Active_but_not_yet_InEvaluation_returns_409_invalid_state_transition()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedTableWithSamplesAsync(organizer, judgeCount: 1);
        await TransitionStateAsync(organizer, fixture.CompetitionId, "Active");

        var response = await RemoveJudgeAsync(organizer, fixture.CompetitionId, fixture.TableId, fixture.Judges[0].JudgeId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    // ---- Concurrency: the FOR UPDATE lock must survive a real race, not just sequential replay -----

    [Fact]
    public async Task Fifty_concurrent_removals_of_the_same_judge_yield_exactly_one_success_and_no_exceptions()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 1);
        var judgeId = fixture.Judges[0].JudgeId;

        var results = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => RemoveJudgeAsync(organizer, fixture.CompetitionId, fixture.TableId, judgeId)));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(49, results.Count(r => r.StatusCode == HttpStatusCode.NotFound));

        var auditLog = await GetAuditLogAsync(RemoveJudgeRules.ComputeAuditEntityId(fixture.TableId, judgeId));
        Assert.Equal("JudgeRemoved", auditLog.Action);
    }

    // ---- Never leaks existence (plain 404s) ---------------------------------------------------------

    [Fact]
    public async Task Remove_a_judge_never_assigned_to_the_table_returns_404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 1);

        var neverAssignedJudgeId = Guid.NewGuid();

        var response = await RemoveJudgeAsync(organizer, fixture.CompetitionId, fixture.TableId, neverAssignedJudgeId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Removing_an_already_removed_judge_returns_404_on_the_second_attempt()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        var judge = fixture.Judges[0];

        var firstRemoval = await RemoveJudgeAsync(organizer, fixture.CompetitionId, fixture.TableId, judge.JudgeId);
        Assert.Equal(HttpStatusCode.OK, firstRemoval.StatusCode);

        var secondRemoval = await RemoveJudgeAsync(organizer, fixture.CompetitionId, fixture.TableId, judge.JudgeId);

        Assert.Equal(HttpStatusCode.NotFound, secondRemoval.StatusCode);
    }

    [Fact]
    public async Task Remove_by_a_non_owning_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(owner);
        var judge = fixture.Judges[0];

        using var otherOrganizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await RemoveJudgeAsync(otherOrganizer, fixture.CompetitionId, fixture.TableId, judge.JudgeId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Already-submitted evaluations stay valid (FR-039) ------------------------------------------

    [Fact]
    public async Task An_evaluation_submitted_before_removal_remains_readable_by_the_organizer_after_removal()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer);
        var removedJudge = fixture.Judges[0];
        var entryId = fixture.EntryIds[0];

        using var judgeClient = JudgeClient(removedJudge.JudgeSub);
        var submit = await SubmitAsync(judgeClient, fixture.TableId, entryId);
        Assert.Equal(HttpStatusCode.Created, submit.StatusCode);

        var removeResponse = await RemoveJudgeAsync(organizer, fixture.CompetitionId, fixture.TableId, removedJudge.JudgeId);
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        var evaluationsResponse = await GetEntryEvaluationsAsync(organizer, fixture.CompetitionId, entryId);

        Assert.Equal(HttpStatusCode.OK, evaluationsResponse.StatusCode);
        using var document = JsonDocument.Parse(await evaluationsResponse.Content.ReadAsStringAsync());
        var evaluations = document.RootElement.GetProperty("evaluations").EnumerateArray().ToList();
        Assert.Contains(evaluations, e => e.GetProperty("total").GetInt32() == 39); // aroma 10 + appearance 2 + flavor 15 + mouthfeel 4 + overall 8
    }

    // ---- Removal reconciles open discrepancies at the table (FR-039 — see RemoveJudge.cs doc) ------

    [Fact]
    public async Task Removing_the_common_outlier_resolves_an_open_discrepancy_once_the_remaining_judges_converge()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 3);
        var entryId = fixture.EntryIds[0];

        using var judgeA = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeB = JudgeClient(fixture.Judges[1].JudgeSub);
        using var judgeC = JudgeClient(fixture.Judges[2].JudgeSub);

        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeA, fixture.TableId, entryId, Scores(10, 2, 16, 4, 8))).StatusCode); // 40
        var opening = await SubmitAsync(judgeB, fixture.TableId, entryId, Scores(5, 1, 10, 2, 2)); // 20, diff 20 -> A & B involved
        Assert.Equal(HttpStatusCode.Created, opening.StatusCode);
        Assert.Equal(
            "PendingConsensus", (await opening.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeC, fixture.TableId, entryId, Scores(11, 3, 17, 4, 9))).StatusCode); // 44, diff to B 24 -> C involved too

        Assert.Equal(1, await CountOpenAlertsAsync(fixture.TableId, entryId));

        var blockedClose = await CloseAsync(judgeA, fixture.TableId);
        Assert.Equal(HttpStatusCode.Conflict, blockedClose.StatusCode);
        using var blockedDocument = JsonDocument.Parse(await blockedClose.Content.ReadAsStringAsync());
        Assert.Contains("discrepancy-open", blockedDocument.RootElement.GetProperty("type").GetString());

        // Removing judge B leaves A (40) and C (44) — only 4 points apart, so the alert should now
        // resolve even though nobody called AdjustEvaluation.
        var removeResponse = await RemoveJudgeAsync(organizer, fixture.CompetitionId, fixture.TableId, fixture.Judges[1].JudgeId);
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        Assert.Equal(0, await CountOpenAlertsAsync(fixture.TableId, entryId));

        var discrepanciesForA = await GetDiscrepanciesAsync(judgeA, fixture.TableId);
        using var forA = JsonDocument.Parse(await discrepanciesForA.Content.ReadAsStringAsync());
        Assert.Empty(forA.RootElement.EnumerateArray());

        var discrepanciesForC = await GetDiscrepanciesAsync(judgeC, fixture.TableId);
        using var forC = JsonDocument.Parse(await discrepanciesForC.Content.ReadAsStringAsync());
        Assert.Empty(forC.RootElement.EnumerateArray());

        var close = await CloseAsync(judgeA, fixture.TableId);
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
    }

    [Fact]
    public async Task Removing_a_judge_uninvolved_in_an_open_discrepancy_leaves_it_open_and_blocks_close()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 3);
        var entryId = fixture.EntryIds[0];

        using var judgeA = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeB = JudgeClient(fixture.Judges[1].JudgeSub);

        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeA, fixture.TableId, entryId, Scores(10, 2, 16, 4, 8))).StatusCode); // 40
        var opening = await SubmitAsync(judgeB, fixture.TableId, entryId, Scores(5, 1, 10, 2, 2)); // 20, diff 20 -> both involved
        Assert.Equal(HttpStatusCode.Created, opening.StatusCode);
        Assert.Equal(1, await CountOpenAlertsAsync(fixture.TableId, entryId));

        // Judge C never submitted anything for this entry and is unrelated to the A/B discrepancy.
        var removeResponse = await RemoveJudgeAsync(organizer, fixture.CompetitionId, fixture.TableId, fixture.Judges[2].JudgeId);
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        Assert.Equal(1, await CountOpenAlertsAsync(fixture.TableId, entryId));
        Assert.All(
            await GetEvaluationStatusesAsync(fixture.TableId, entryId),
            s => Assert.Equal(EvaluationStatus.PendingConsensus, s));

        var discrepanciesForA = await GetDiscrepanciesAsync(judgeA, fixture.TableId);
        using var forA = JsonDocument.Parse(await discrepanciesForA.Content.ReadAsStringAsync());
        Assert.Single(forA.RootElement.EnumerateArray());

        var close = await CloseAsync(judgeA, fixture.TableId);
        Assert.Equal(HttpStatusCode.Conflict, close.StatusCode);
        using var closeDocument = JsonDocument.Parse(await close.Content.ReadAsStringAsync());
        Assert.Contains("discrepancy-open", closeDocument.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Removing_one_of_three_mutually_divergent_judges_leaves_the_alert_open_when_the_remaining_two_still_diverge()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 3);
        var entryId = fixture.EntryIds[0];

        using var judgeA = JudgeClient(fixture.Judges[0].JudgeSub);
        using var judgeB = JudgeClient(fixture.Judges[1].JudgeSub);
        using var judgeC = JudgeClient(fixture.Judges[2].JudgeSub);

        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeA, fixture.TableId, entryId, Scores(1, 0, 2, 1, 1))).StatusCode); // 5
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeB, fixture.TableId, entryId, Scores(6, 1, 12, 3, 3))).StatusCode); // 25, diff to A 20 -> both involved
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeC, fixture.TableId, entryId, Scores(11, 3, 17, 5, 9))).StatusCode); // 45, diff to A 40, diff to B 20 -> all three involved

        Assert.Equal(1, await CountOpenAlertsAsync(fixture.TableId, entryId));

        // Removing judge A leaves B (25) and C (45) — still 20 points apart, so the alert must stay
        // open; it is not force-resolved just because one party left.
        var removeResponse = await RemoveJudgeAsync(organizer, fixture.CompetitionId, fixture.TableId, fixture.Judges[0].JudgeId);
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        Assert.Equal(1, await CountOpenAlertsAsync(fixture.TableId, entryId));

        var discrepanciesForB = await GetDiscrepanciesAsync(judgeB, fixture.TableId);
        using var forB = JsonDocument.Parse(await discrepanciesForB.Content.ReadAsStringAsync());
        Assert.Single(forB.RootElement.EnumerateArray());

        var close = await CloseAsync(judgeB, fixture.TableId);
        Assert.Equal(HttpStatusCode.Conflict, close.StatusCode);
        using var closeDocument = JsonDocument.Parse(await close.Content.ReadAsStringAsync());
        Assert.Contains("discrepancy-open", closeDocument.RootElement.GetProperty("type").GetString());
    }
}
