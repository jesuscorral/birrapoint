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
}
