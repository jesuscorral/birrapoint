using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Judging.Features.Tables.Features.UpdateTable;
using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Judging.Tables.Features.UpdateTable;

/// <summary>
/// HTTP-level contract tests for live judge removal via PUT /competitions/{id}/tables/{tableId}
/// (contracts/rest-api.md §Tables, FR-039; replaces the removed DELETE .../judges/{judgeId}).
/// In InEvaluation the PUT is accepted ONLY when it solely removes currently-active judges (same
/// name, same samples, judgeIds a subset of the active set). Removal revokes the judge's access
/// immediately, is audit-logged per judge, re-reconciles open discrepancies, never leaks existence
/// to a non-owning organizer, and leaves already-submitted evaluations valid.
/// </summary>
public sealed class UpdateTableLiveJudgeRemovalApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
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
    private async Task FixOrderDirectlyAsync(Guid tableId, Guid judgeId, List<Guid> orderedEntryIds)
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

    private static async Task<HttpResponseMessage> SubmitAsync(
        HttpClient client, Guid tableId, Guid beerEntryId, object? scores = null, object? comments = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/me/tables/{tableId}/evaluations")
        {
            Content = JsonContent.Create(new { beerEntryId, scores = scores ?? ValidScores(), comments = comments ?? ValidComments() }),
        };
        request.Headers.Add("X-Idempotency-Key", $"comp:{tableId}:judge:{beerEntryId}");
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> GetSamplesAsync(HttpClient client, Guid tableId) =>
        client.GetAsync(Urls.Rel($"/api/v1/me/tables/{tableId}/samples"));

    private static Task<HttpResponseMessage> PutTableAsync(
        HttpClient client, Guid competitionId, Guid tableId, string name, IEnumerable<Guid> judgeIds, IEnumerable<Guid> beerEntryIds) =>
        client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/tables/{tableId}", new { name, judgeIds, beerEntryIds });

    /// <summary>Builds the PUT body that keeps the table's current name and samples and the current
    /// active judges minus <paramref name="removedJudgeIds"/> — i.e. a pure live removal.</summary>
    private async Task<HttpResponseMessage> RemoveJudgesAsync(
        HttpClient client, Guid competitionId, Guid tableId, params Guid[] removedJudgeIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = await db.TastingTables.AsNoTracking().Where(t => t.Id == tableId).Select(t => t.Name).SingleAsync();
        var activeJudges = await db.TableJudges.AsNoTracking()
            .Where(tj => tj.TastingTableId == tableId && tj.RemovedAt == null).Select(tj => tj.JudgeId).ToListAsync();
        var entries = await db.TableSamples.AsNoTracking()
            .Where(ts => ts.TastingTableId == tableId).Select(ts => ts.BeerEntryId).ToListAsync();
        return await PutTableAsync(client, competitionId, tableId, name, activeJudges.Except(removedJudgeIds), entries);
    }

    private Task<HttpResponseMessage> RemoveJudgeAsync(HttpClient client, Guid competitionId, Guid tableId, Guid judgeId) =>
        RemoveJudgesAsync(client, competitionId, tableId, judgeId);

    private async Task<(string Name, List<Guid> ActiveJudges, List<Guid> Samples, int RemovedCount)> SnapshotAsync(Guid tableId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = await db.TastingTables.AsNoTracking().Where(t => t.Id == tableId).Select(t => t.Name).SingleAsync();
        var rows = await db.TableJudges.AsNoTracking().Where(tj => tj.TastingTableId == tableId).ToListAsync();
        var samples = await db.TableSamples.AsNoTracking().Where(ts => ts.TastingTableId == tableId).Select(ts => ts.BeerEntryId).ToListAsync();
        return (name, rows.Where(r => r.RemovedAt is null).Select(r => r.JudgeId).ToList(), samples, rows.Count(r => r.RemovedAt is not null));
    }

    private async Task<int> CountAuditRowsAsync(Guid tableId, Guid judgeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entityId = RemoveJudgeRules.ComputeAuditEntityId(tableId, judgeId);
        return await db.AuditLogs.AsNoTracking().CountAsync(a => a.EntityId == entityId);
    }

    private static async Task AssertProblemTypeAsync(HttpResponseMessage response, HttpStatusCode status, string type)
    {
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(type, document.RootElement.GetProperty("type").GetString());
    }

    private static Task<HttpResponseMessage> GetEntryEvaluationsAsync(HttpClient client, Guid competitionId, Guid entryId) =>
        client.GetAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/entries/{entryId}/evaluations"));

    // ---- Discrepancy-reconciliation-on-removal helpers (mirrors DiscrepancyApiTests.cs) -----------

    private static object Scores(int aroma, int appearance, int flavor, int mouthfeel, int overall) =>
        new { aroma, appearance, flavor, mouthfeel, overall };

    private static Task<HttpResponseMessage> GetDiscrepanciesAsync(HttpClient client, Guid tableId) =>
        client.GetAsync(Urls.Rel($"/api/v1/me/tables/{tableId}/discrepancies"));

    private static Task<HttpResponseMessage> CloseAsync(HttpClient client, Guid tableId) =>
        client.PostAsync(Urls.Rel($"/api/v1/me/tables/{tableId}/close"), content: null);

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
    public async Task RemoveByTheOwningOrganizerReturns200AndImmediatelyRevokesTheRemovedJudgesTableAccess()
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
    public async Task RemoveWritesAnAuditLogRowKeyedByTableAndJudge()
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

    [Fact]
    public async Task RemoveByANonOwningOrganizerReturns404()
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
    public async Task AnEvaluationSubmittedBeforeRemovalRemainsReadableByTheOrganizerAfterRemoval()
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
        using var document = JsonDocument.Parse(await evaluationsResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var evaluations = document.RootElement.GetProperty("evaluations").EnumerateArray().ToList();
        Assert.Contains(evaluations, e => e.GetProperty("total").GetInt32() == 39); // aroma 10 + appearance 2 + flavor 15 + mouthfeel 4 + overall 8
    }

    // ---- Removal reconciles open discrepancies at the table (FR-039 — see RemoveJudge.cs doc) ------

    [Fact]
    public async Task RemovingTheCommonOutlierResolvesAnOpenDiscrepancyOnceTheRemainingJudgesConverge()
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
            "PendingConsensus", (await opening.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(judgeC, fixture.TableId, entryId, Scores(11, 3, 17, 4, 9))).StatusCode); // 44, diff to B 24 -> C involved too

        Assert.Equal(1, await CountOpenAlertsAsync(fixture.TableId, entryId));

        var blockedClose = await CloseAsync(judgeA, fixture.TableId);
        Assert.Equal(HttpStatusCode.Conflict, blockedClose.StatusCode);
        using var blockedDocument = JsonDocument.Parse(await blockedClose.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("discrepancy-open", blockedDocument.RootElement.GetProperty("type").GetString(), StringComparison.Ordinal);

        // Removing judge B leaves A (40) and C (44) — only 4 points apart, so the alert should now
        // resolve even though nobody called AdjustEvaluation.
        var removeResponse = await RemoveJudgeAsync(organizer, fixture.CompetitionId, fixture.TableId, fixture.Judges[1].JudgeId);
        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);

        Assert.Equal(0, await CountOpenAlertsAsync(fixture.TableId, entryId));

        var discrepanciesForA = await GetDiscrepanciesAsync(judgeA, fixture.TableId);
        using var forA = JsonDocument.Parse(await discrepanciesForA.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(forA.RootElement.EnumerateArray());

        var discrepanciesForC = await GetDiscrepanciesAsync(judgeC, fixture.TableId);
        using var forC = JsonDocument.Parse(await discrepanciesForC.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(forC.RootElement.EnumerateArray());

        var close = await CloseAsync(judgeA, fixture.TableId);
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
    }

    [Fact]
    public async Task RemovingAJudgeUninvolvedInAnOpenDiscrepancyLeavesItOpenAndBlocksClose()
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
        using var forA = JsonDocument.Parse(await discrepanciesForA.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Single(forA.RootElement.EnumerateArray());

        var close = await CloseAsync(judgeA, fixture.TableId);
        Assert.Equal(HttpStatusCode.Conflict, close.StatusCode);
        using var closeDocument = JsonDocument.Parse(await close.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("discrepancy-open", closeDocument.RootElement.GetProperty("type").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemovingOneOfThreeMutuallyDivergentJudgesLeavesTheAlertOpenWhenTheRemainingTwoStillDiverge()
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
        using var forB = JsonDocument.Parse(await discrepanciesForB.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Single(forB.RootElement.EnumerateArray());

        var close = await CloseAsync(judgeB, fixture.TableId);
        Assert.Equal(HttpStatusCode.Conflict, close.StatusCode);
        using var closeDocument = JsonDocument.Parse(await close.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("discrepancy-open", closeDocument.RootElement.GetProperty("type").GetString(), StringComparison.Ordinal);
    }

    // ---- Concurrency: the FOR UPDATE lock must survive a real race, not just sequential replay -----

    [Fact]
    public async Task FiftyConcurrentPutsRemovingTheSameJudgeWriteExactlyOneAuditRowAndNoServerErrors()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);
        var judgeId = fixture.Judges[0].JudgeId;

        var results = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => RemoveJudgesAsync(organizer, fixture.CompetitionId, fixture.TableId, judgeId)));

        Assert.All(results, r => Assert.True(
            r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"unexpected {(int)r.StatusCode}"));
        Assert.Contains(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, await CountAuditRowsAsync(fixture.TableId, judgeId));
        Assert.Equal(1, (await SnapshotAsync(fixture.TableId)).RemovedCount);
    }

    // ---- Response shape ----------------------------------------------------------------------------

    [Fact]
    public async Task RemovalResponseIsTheTableMutationResultWithoutTheRemovedJudge()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);
        var removed = fixture.Judges[0].JudgeId;
        var kept = fixture.Judges[1].JudgeId;

        var response = await RemoveJudgesAsync(organizer, fixture.CompetitionId, fixture.TableId, removed);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(fixture.TableId, body.GetProperty("id").GetGuid());
        Assert.True(body.TryGetProperty("bosFlaggedEntryIds", out _));
        var ids = body.GetProperty("judges").EnumerateArray()
            .Select(j => j.TryGetProperty("judgeId", out var p) ? p.GetGuid() : j.GetProperty("id").GetGuid()).ToList();
        Assert.DoesNotContain(removed, ids);
        Assert.Contains(kept, ids);
    }

    // ---- Multi-judge removal ----------------------------------------------------------------------

    [Fact]
    public async Task OnePutRemovingTwoJudgesSoftRemovesBothAuditsEachAndRevokesBothAccesses()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 3);
        var (removedA, removedB, kept) = (fixture.Judges[0], fixture.Judges[1], fixture.Judges[2]);

        var response = await RemoveJudgesAsync(organizer, fixture.CompetitionId, fixture.TableId, removedA.JudgeId, removedB.JudgeId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await SnapshotAsync(fixture.TableId);
        Assert.Equal([kept.JudgeId], snapshot.ActiveJudges);
        Assert.Equal(2, snapshot.RemovedCount);
        Assert.Equal(1, await CountAuditRowsAsync(fixture.TableId, removedA.JudgeId));
        Assert.Equal(1, await CountAuditRowsAsync(fixture.TableId, removedB.JudgeId));

        using var clientA = JudgeClient(removedA.JudgeSub);
        using var clientB = JudgeClient(removedB.JudgeSub);
        using var clientKept = JudgeClient(kept.JudgeSub);
        Assert.Equal(HttpStatusCode.NotFound, (await GetSamplesAsync(clientA, fixture.TableId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetSamplesAsync(clientB, fixture.TableId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetSamplesAsync(clientKept, fixture.TableId)).StatusCode);
    }

    // ---- No-op ---------------------------------------------------------------------------------------

    [Fact]
    public async Task InEvaluationPutWithTheCurrentStateIsANoOpReturning200WithoutAuditOrRemoval()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);

        var response = await RemoveJudgesAsync(organizer, fixture.CompetitionId, fixture.TableId); // removes nobody

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await SnapshotAsync(fixture.TableId);
        Assert.Equal(2, snapshot.ActiveJudges.Count);
        Assert.Equal(0, snapshot.RemovedCount);
        foreach (var judge in fixture.Judges)
        {
            Assert.Equal(0, await CountAuditRowsAsync(fixture.TableId, judge.JudgeId));
        }
    }

    [Fact]
    public async Task RepeatingTheSameRemovalPutIsAnIdempotent200NoOpWithASingleAuditRow()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);
        var judge = fixture.Judges[0];

        Assert.Equal(HttpStatusCode.OK, (await RemoveJudgesAsync(organizer, fixture.CompetitionId, fixture.TableId, judge.JudgeId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RemoveJudgesAsync(organizer, fixture.CompetitionId, fixture.TableId, judge.JudgeId)).StatusCode);

        Assert.Equal(1, await CountAuditRowsAsync(fixture.TableId, judge.JudgeId));
        Assert.Equal(1, (await SnapshotAsync(fixture.TableId)).RemovedCount);
    }

    // ---- InEvaluation: anything other than a pure removal is rejected, nothing changes ---------------

    [Fact]
    public async Task InEvaluationPutThatRenamesTheTableReturns409AndChangesNothing()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);
        var before = await SnapshotAsync(fixture.TableId);

        var response = await PutTableAsync(
            organizer, fixture.CompetitionId, fixture.TableId, "Renamed live", before.ActiveJudges, before.Samples);

        await AssertProblemTypeAsync(response, HttpStatusCode.Conflict, "urn:birrapoint:invalid-state-transition");
        var after = await SnapshotAsync(fixture.TableId);
        Assert.Equal(before.Name, after.Name);
        Assert.Equal(0, after.RemovedCount);
    }

    [Fact]
    public async Task InEvaluationPutThatChangesTheSamplesReturns409AndChangesNothing()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, sampleCount: 2, judgeCount: 2);
        var before = await SnapshotAsync(fixture.TableId);

        var response = await PutTableAsync(
            organizer, fixture.CompetitionId, fixture.TableId, before.Name, before.ActiveJudges, [fixture.EntryIds[0]]);

        await AssertProblemTypeAsync(response, HttpStatusCode.Conflict, "urn:birrapoint:invalid-state-transition");
        var after = await SnapshotAsync(fixture.TableId);
        Assert.Equal(before.Samples.Order(), after.Samples.Order());
    }

    [Fact]
    public async Task InEvaluationPutThatAddsAJudgeReturns409AndChangesNothing()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 1);
        var extraSub = $"extra-{Guid.NewGuid():N}";
        var extraJudgeId = await SeedJudgeAsync(fixture.CompetitionId, $"{extraSub}@brew.example", extraSub);
        var before = await SnapshotAsync(fixture.TableId);

        var response = await PutTableAsync(
            organizer, fixture.CompetitionId, fixture.TableId, before.Name, [.. before.ActiveJudges, extraJudgeId], before.Samples);

        await AssertProblemTypeAsync(response, HttpStatusCode.Conflict, "urn:birrapoint:invalid-state-transition");
        var after = await SnapshotAsync(fixture.TableId);
        Assert.Equal(before.ActiveJudges, after.ActiveJudges);
    }

    [Fact]
    public async Task InEvaluationPutThatMixesARemovalWithARenameReturns409AndTheJudgeStaysActive()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);
        var before = await SnapshotAsync(fixture.TableId);

        var response = await PutTableAsync(
            organizer, fixture.CompetitionId, fixture.TableId, "Renamed live", [fixture.Judges[1].JudgeId], before.Samples);

        await AssertProblemTypeAsync(response, HttpStatusCode.Conflict, "urn:birrapoint:invalid-state-transition");
        var after = await SnapshotAsync(fixture.TableId);
        Assert.Equal(2, after.ActiveJudges.Count);
        Assert.Equal(0, after.RemovedCount);
        Assert.Equal(0, await CountAuditRowsAsync(fixture.TableId, fixture.Judges[0].JudgeId));
    }

    [Fact]
    public async Task InEvaluationPutReAddingAnAlreadyRemovedJudgeReturns409()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);
        var removed = fixture.Judges[0].JudgeId;
        Assert.Equal(HttpStatusCode.OK, (await RemoveJudgesAsync(organizer, fixture.CompetitionId, fixture.TableId, removed)).StatusCode);
        var current = await SnapshotAsync(fixture.TableId);

        var response = await PutTableAsync(
            organizer, fixture.CompetitionId, fixture.TableId, current.Name, [.. current.ActiveJudges, removed], current.Samples);

        await AssertProblemTypeAsync(response, HttpStatusCode.Conflict, "urn:birrapoint:invalid-state-transition");
        Assert.Equal(1, (await SnapshotAsync(fixture.TableId)).RemovedCount);
    }

    [Fact]
    public async Task InEvaluationPutNamingAJudgeNeverAssignedToTheTableReturns409()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 1);
        var before = await SnapshotAsync(fixture.TableId);

        var response = await PutTableAsync(
            organizer, fixture.CompetitionId, fixture.TableId, before.Name, [Guid.NewGuid()], before.Samples);

        await AssertProblemTypeAsync(response, HttpStatusCode.Conflict, "urn:birrapoint:invalid-state-transition");
    }

    // ---- Other gates ----------------------------------------------------------------------------------

    [Fact]
    public async Task InEvaluationPutOnAClosedTableReturns409TableClosed()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var table = await db.TastingTables.SingleAsync(t => t.Id == fixture.TableId, TestContext.Current.CancellationToken);
            table.State = TableState.Closed;
            table.ClosedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var response = await RemoveJudgesAsync(organizer, fixture.CompetitionId, fixture.TableId, fixture.Judges[0].JudgeId);

        await AssertProblemTypeAsync(response, HttpStatusCode.Conflict, "urn:birrapoint:table-closed");
        Assert.Equal(0, (await SnapshotAsync(fixture.TableId)).RemovedCount);
    }

    [Fact]
    public async Task PutOnAFinalizedCompetitionReturns409InvalidStateTransition()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var competition = await db.Competitions.SingleAsync(c => c.Id == fixture.CompetitionId, TestContext.Current.CancellationToken);
            competition.State = CompetitionState.Finalized;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var response = await RemoveJudgesAsync(organizer, fixture.CompetitionId, fixture.TableId, fixture.Judges[0].JudgeId);

        await AssertProblemTypeAsync(response, HttpStatusCode.Conflict, "urn:birrapoint:invalid-state-transition");
        Assert.Equal(0, (await SnapshotAsync(fixture.TableId)).RemovedCount);
    }

    [Fact]
    public async Task PutAddressingATableOfAnotherCompetitionReturns404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var fixture = await SeedReadyTableAsync(organizer, judgeCount: 2);
        var otherCompetitionId = await CreateCompetitionAsync(organizer, "Other");

        var response = await PutTableAsync(
            organizer, otherCompetitionId, fixture.TableId, "X", [fixture.Judges[1].JudgeId], fixture.EntryIds);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, (await SnapshotAsync(fixture.TableId)).RemovedCount);
    }
}
