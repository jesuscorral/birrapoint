using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.TastingOrder;

/// <summary>
/// T051: HTTP-level contract tests for the judge-facing tasting-order workspace
/// (contracts/rest-api.md §Judge workspace) — active-membership scoping on GET /me/tables and
/// GET /me/tables/{tableId}/samples, the BR-01 anonymity boundary on the serialized wire payload
/// (not just the DTO's declared shape), and the one-shot POST /me/tables/{tableId}/order race
/// (US6-4 / Clarification Q1).
/// </summary>
public sealed class OrderApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string StyleCodeApa = "21A";

    private HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    /// <summary>
    /// ICurrentUser.GetJudgeRecordsAsync (JudgeResolver.ResolveAndBackfillAsync) resolves Judge
    /// rows by email, not by `sub` — KeycloakUserId is only ever used to backfill, never as a
    /// lookup key (see Common/Auth/JudgeResolver.cs). So the test JWT must carry the same email
    /// the Judge row was seeded with; every SeedJudgeAsync call below uses the deterministic
    /// `{sub}@brew.example` convention this derives, keeping seed and client in lockstep.
    /// </summary>
    private HttpClient JudgeClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, $"{sub}@brew.example", "JUDGE"));
        return client;
    }

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Order")
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

    /// <summary>Seeds a Judge with a pre-set KeycloakUserId so a test JWT minted with the same
    /// `sub` authenticates as this judge without a real Keycloak login round-trip — KeycloakUserId
    /// is otherwise only backfilled by a real login through JudgeResolver (same constraint
    /// documented by JudgesApiTests.SeedJudgeAsync).</summary>
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

    private async Task SeedTableJudgeAsync(Guid tableId, Guid judgeId, DateTimeOffset? removedAt = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableJudges.Add(new TableJudge { TastingTableId = tableId, JudgeId = judgeId, RemovedAt = removedAt });
        await db.SaveChangesAsync();
    }

    private async Task SeedTableSampleAsync(Guid tableId, Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableSamples.Add(new TableSample { TastingTableId = tableId, BeerEntryId = beerEntryId });
        await db.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> GetMyTablesAsync(HttpClient client) =>
        client.GetAsync("/api/v1/me/tables");

    private static Task<HttpResponseMessage> GetTableSamplesAsync(HttpClient client, Guid tableId) =>
        client.GetAsync($"/api/v1/me/tables/{tableId}/samples");

    private static Task<HttpResponseMessage> FixOrderAsync(HttpClient client, Guid tableId, IEnumerable<Guid> orderedBeerEntryIds) =>
        client.PostAsJsonAsync($"/api/v1/me/tables/{tableId}/order", new { orderedBeerEntryIds });

    /// <summary>Seeds a competition already Active, one table with <paramref name="sampleCount"/>
    /// samples, and one judge actively assigned to it — the common fixture for the
    /// GET .../samples / POST .../order tests below.</summary>
    private async Task<(Guid CompetitionId, Guid TableId, Guid JudgeId, string JudgeSub, List<Guid> EntryIds)>
        SeedActiveTableWithSamplesAsync(HttpClient organizer, int sampleCount = 2)
    {
        var competitionId = await CreateCompetitionAsync(organizer);
        await TransitionStateAsync(organizer, competitionId, "Active");

        var tableId = await SeedTableAsync(competitionId, $"Table {Guid.NewGuid():N}");

        var judgeSub = $"judge-{Guid.NewGuid():N}";
        var judgeEmail = $"{judgeSub}@brew.example";
        var judgeId = await SeedJudgeAsync(competitionId, judgeEmail, judgeSub);
        await SeedTableJudgeAsync(tableId, judgeId);

        var entryIds = new List<Guid>();
        for (var i = 0; i < sampleCount; i++)
        {
            var participantId = await SeedParticipantAsync(
                competitionId, $"Brewer {i}", $"brewer-{Guid.NewGuid():N}@brew.example");
            var entryId = await SeedBeerEntryAsync(competitionId, participantId, $"Secret Beer {i}");
            await SeedTableSampleAsync(tableId, entryId);
            entryIds.Add(entryId);
        }

        return (competitionId, tableId, judgeId, judgeSub, entryIds);
    }

    // ---- GET /me/tables -------------------------------------------------------------------------

    [Fact]
    public async Task GetMyTables_returns_empty_list_for_a_judge_with_no_assignments()
    {
        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");

        var response = await GetMyTablesAsync(judge);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(document.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task GetMyTables_returns_only_tables_the_judge_is_actively_assigned_to()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (competitionId, tableId, _, judgeSub, _) = await SeedActiveTableWithSamplesAsync(organizer);

        // An unrelated table in the same competition the judge is NOT assigned to.
        await SeedTableAsync(competitionId, $"Other {Guid.NewGuid():N}");

        using var judge = JudgeClient(judgeSub);
        var response = await GetMyTablesAsync(judge);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tables = document.RootElement.EnumerateArray().ToList();
        var table = Assert.Single(tables);
        Assert.Equal(tableId, table.GetProperty("tableId").GetGuid());
        Assert.False(table.GetProperty("orderFixed").GetBoolean());
    }

    [Fact]
    public async Task GetMyTables_excludes_tables_where_the_judge_was_removed()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        await TransitionStateAsync(organizer, competitionId, "Active");
        var tableId = await SeedTableAsync(competitionId, $"Table {Guid.NewGuid():N}");

        var judgeSub = $"judge-{Guid.NewGuid():N}";
        var judgeId = await SeedJudgeAsync(competitionId, $"{judgeSub}@brew.example", judgeSub);
        await SeedTableJudgeAsync(tableId, judgeId, removedAt: DateTimeOffset.UtcNow);

        using var judge = JudgeClient(judgeSub);
        var response = await GetMyTablesAsync(judge);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(document.RootElement.EnumerateArray());
    }

    /// <summary>contracts/rest-api.md §Judge workspace: "Competition must be Active+ (invisible
    /// in Draft)".</summary>
    [Fact]
    public async Task GetMyTables_hides_tables_of_a_competition_still_in_draft()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var tableId = await SeedTableAsync(competitionId, $"Table {Guid.NewGuid():N}");

        var judgeSub = $"judge-{Guid.NewGuid():N}";
        var judgeId = await SeedJudgeAsync(competitionId, $"{judgeSub}@brew.example", judgeSub);
        await SeedTableJudgeAsync(tableId, judgeId);

        using var judge = JudgeClient(judgeSub);
        var response = await GetMyTablesAsync(judge);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(document.RootElement.EnumerateArray());
    }

    // ---- GET /me/tables/{tableId}/samples ---------------------------------------------------------

    [Fact]
    public async Task GetTableSamples_for_a_table_the_judge_is_not_assigned_to_returns_404()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (_, tableId, _, _, _) = await SeedActiveTableWithSamplesAsync(organizer);

        using var otherJudge = JudgeClient($"outsider-{Guid.NewGuid():N}");
        var response = await GetTableSamplesAsync(otherJudge, tableId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>BR-01/FR-019 structural test — asserts the serialized wire payload, not just the
    /// declared DTO shape, never carries entrant fields.</summary>
    [Fact]
    public async Task GetTableSamples_payload_never_contains_entrant_fields()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (_, tableId, _, judgeSub, _) = await SeedActiveTableWithSamplesAsync(organizer, sampleCount: 3);

        using var judge = JudgeClient(judgeSub);
        var response = await GetTableSamplesAsync(judge, tableId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        var lowered = raw.ToLowerInvariant();
        foreach (var forbidden in new[] { "beername", "participant", "brewery", "origin", "collaborator" })
        {
            Assert.DoesNotContain(forbidden, lowered);
        }
    }

    // ---- POST /me/tables/{tableId}/order -----------------------------------------------------------

    [Fact]
    public async Task FixOrder_happy_path_assigns_sequence_and_locks_the_table()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (_, tableId, _, judgeSub, entryIds) = await SeedActiveTableWithSamplesAsync(organizer, sampleCount: 3);

        using var judge = JudgeClient(judgeSub);
        var response = await FixOrderAsync(judge, tableId, entryIds);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var samples = document.RootElement.EnumerateArray().ToList();
        Assert.Equal(entryIds.Count, samples.Count);
        Assert.All(samples, s => Assert.Equal(JsonValueKind.Number, s.GetProperty("sequenceOrder").ValueKind));

        var tablesResponse = await GetMyTablesAsync(judge);
        using var tablesDocument = JsonDocument.Parse(await tablesResponse.Content.ReadAsStringAsync());
        var table = tablesDocument.RootElement.EnumerateArray().Single(t => t.GetProperty("tableId").GetGuid() == tableId);
        Assert.True(table.GetProperty("orderFixed").GetBoolean());
    }

    [Fact]
    public async Task FixOrder_with_a_non_permutation_body_returns_400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (_, tableId, _, judgeSub, entryIds) = await SeedActiveTableWithSamplesAsync(organizer, sampleCount: 2);

        using var judge = JudgeClient(judgeSub);
        var response = await FixOrderAsync(judge, tableId, [entryIds[0]]); // missing one id

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>US6-4 / Clarification Q1: two judges race to fix the same table's order —
    /// exactly one wins, the other gets 409 order-already-fixed.</summary>
    [Fact]
    public async Task FixOrder_race_only_one_concurrent_request_wins()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (_, tableId, _, judgeSub, entryIds) = await SeedActiveTableWithSamplesAsync(organizer, sampleCount: 3);

        using var judgeA = JudgeClient(judgeSub);
        using var judgeB = JudgeClient(judgeSub);

        var reversed = entryIds.AsEnumerable().Reverse().ToList();

        var taskA = FixOrderAsync(judgeA, tableId, entryIds);
        var taskB = FixOrderAsync(judgeB, tableId, reversed);
        var responses = await Task.WhenAll(taskA, taskB);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var conflictResponse = responses.First(r => r.StatusCode == HttpStatusCode.Conflict);
        using var document = JsonDocument.Parse(await conflictResponse.Content.ReadAsStringAsync());
        Assert.Contains("order-already-fixed", document.RootElement.GetProperty("type").GetString());
    }
}
