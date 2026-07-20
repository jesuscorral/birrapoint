using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Tables;

/// <summary>
/// T046: HTTP-level contract tests for the Tables slices (contracts/rest-api.md §Tables) —
/// transactional conflict-of-interest rejection with atomic rollback (FR-017), "Not valid for
/// BOS" flagging/unflagging on table membership (FR-018), and the entry-in-one-table /
/// table-name uniqueness constraints (data-model.md §TastingTable/§TableSample). T047 (the
/// CreateTable/UpdateTable/ListTables slices) is landing in parallel — failures against
/// non-existent `/api/v1/competitions/{id}/tables` routes are expected until that work merges.
/// </summary>
public sealed class TablesApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string StyleCodeApa = "21A";

    private HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    private HttpClient JudgeClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "JUDGE"));
        return client;
    }

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Tables")
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

    private static Task<HttpResponseMessage> CreateTableAsync(
        HttpClient client, Guid competitionId, string name, IEnumerable<Guid> judgeIds, IEnumerable<Guid> beerEntryIds) =>
        client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/tables", new { name, judgeIds, beerEntryIds });

    private static Task<HttpResponseMessage> UpdateTableAsync(
        HttpClient client, Guid competitionId, Guid tableId, string name, IEnumerable<Guid> judgeIds, IEnumerable<Guid> beerEntryIds) =>
        client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/tables/{tableId}", new { name, judgeIds, beerEntryIds });

    private static Task<HttpResponseMessage> GetTablesAsync(HttpClient client, Guid competitionId) =>
        client.GetAsync($"/api/v1/competitions/{competitionId}/tables");

    /// <summary>Extracts a set of ids from a JSON array whose elements are either raw GUID
    /// strings or objects carrying one of <paramref name="idPropertyNames"/> — the GET /tables
    /// item shape for judges/samples isn't pinned exactly by the contract doc, so this tolerates
    /// either representation while still asserting real identity, not just counts.</summary>
    private static HashSet<Guid> ExtractIds(JsonElement array, params string[] idPropertyNames)
    {
        var ids = new HashSet<Guid>();
        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                ids.Add(element.GetGuid());
                continue;
            }

            foreach (var name in idPropertyNames)
            {
                if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    ids.Add(prop.GetGuid());
                    break;
                }
            }
        }

        return ids;
    }

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
        Guid competitionId, Guid participantId, string beerName, string styleCode = StyleCodeApa, IEnumerable<string>? collaboratorEmails = null)
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
        foreach (var email in collaboratorEmails ?? [])
        {
            db.EntryCollaborators.Add(new EntryCollaborator { BeerEntryId = entry.Id, Email = email });
        }

        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<Guid> SeedJudgeAsync(Guid competitionId, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judge = new Judge { CompetitionId = competitionId, Email = email, DisplayName = email.Split('@')[0] };
        db.Judges.Add(judge);
        await db.SaveChangesAsync();
        return judge.Id;
    }

    private async Task<Guid> SeedClosedTableAsync(Guid competitionId, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var table = new TastingTable
        {
            CompetitionId = competitionId,
            Name = name,
            State = TableState.Closed,
            ClosedAt = DateTimeOffset.UtcNow,
        };
        db.TastingTables.Add(table);
        await db.SaveChangesAsync();
        return table.Id;
    }

    private async Task<bool> GetNotValidForBosAsync(Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.BeerEntries.AsNoTracking().SingleAsync(e => e.Id == beerEntryId);
        return entry.NotValidForBos;
    }

    // ---- POST /tables: auth & ownership -------------------------------------------------------

    [Fact]
    public async Task Create_without_a_bearer_token_is_rejected_with_401()
    {
        using var client = factory.CreateClient();

        var response = await CreateTableAsync(client, Guid.NewGuid(), "Table 1", [], []);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await CreateTableAsync(judge, competitionId, "Table 1", [], []);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_for_a_competition_owned_by_a_different_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var participantId = await SeedParticipantAsync(competitionId, "Ana Gomez", $"ana-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Hop Cannon");
        var judgeId = await SeedJudgeAsync(competitionId, $"judge-{Guid.NewGuid():N}@brew.example");

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await CreateTableAsync(other, competitionId, "Table 1", [judgeId], [entryId]);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- POST /tables: success, COI rejection, BOS flagging -----------------------------------

    [Fact]
    public async Task Create_with_no_conflicts_succeeds_and_persists_judges_and_entries()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var participantId = await SeedParticipantAsync(competitionId, "Ana Gomez", $"ana-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Hop Cannon");
        var judgeId = await SeedJudgeAsync(competitionId, $"judge-{Guid.NewGuid():N}@brew.example");
        var tableName = $"Table {Guid.NewGuid():N}";

        var response = await CreateTableAsync(organizer, competitionId, tableName, [judgeId], [entryId]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotEqual(Guid.Empty, created.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(tableName, created.RootElement.GetProperty("name").GetString());
        Assert.Equal("Open", created.RootElement.GetProperty("state").GetString());

        // No FR-018 trigger here — the judge has no entries of their own in the competition.
        if (created.RootElement.TryGetProperty("bosFlaggedEntryIds", out var bosFlagged))
        {
            Assert.Empty(bosFlagged.EnumerateArray());
        }

        var listResponse = await GetTablesAsync(organizer, competitionId);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var tables = list.RootElement.EnumerateArray().ToList();
        Assert.Single(tables);
        var table = tables[0];
        Assert.Equal(tableName, table.GetProperty("name").GetString());
        Assert.Contains(judgeId, ExtractIds(table.GetProperty("judges"), "judgeId", "id"));
        Assert.Contains(entryId, ExtractIds(table.GetProperty("samples"), "beerEntryId", "id"));
    }

    [Fact]
    public async Task Create_flags_all_of_the_judges_entries_as_not_valid_for_bos_without_a_direct_collision()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var judgeEmail = $"judge-owner-{Guid.NewGuid():N}@brew.example";
        var judgeAsParticipantId = await SeedParticipantAsync(competitionId, "Judge Brewer", judgeEmail);
        // Owned by the judge but NOT assigned to this table — the "elsewhere in the
        // competition" case from FR-018 / spec.md US5 Acceptance Scenario 3.
        var elsewhereEntryId = await SeedBeerEntryAsync(competitionId, judgeAsParticipantId, "Judge's Own Brew");

        var otherParticipantId = await SeedParticipantAsync(competitionId, "Other Brewer", $"other-{Guid.NewGuid():N}@brew.example");
        var tableEntryId = await SeedBeerEntryAsync(competitionId, otherParticipantId, "Unrelated Brew");

        var judgeId = await SeedJudgeAsync(competitionId, judgeEmail);

        var response = await CreateTableAsync(organizer, competitionId, $"Table {Guid.NewGuid():N}", [judgeId], [tableEntryId]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var bosFlagged = created.RootElement.GetProperty("bosFlaggedEntryIds").EnumerateArray()
            .Select(e => e.GetGuid()).ToHashSet();
        Assert.Contains(elsewhereEntryId, bosFlagged);
        Assert.DoesNotContain(tableEntryId, bosFlagged); // no direct collision, so it wasn't rejected

        Assert.True(await GetNotValidForBosAsync(elsewhereEntryId));
    }

    [Fact]
    public async Task Create_with_a_direct_owner_collision_is_rejected_with_409_and_nothing_persisted()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var conflictingEmail = $"owner-{Guid.NewGuid():N}@brew.example";
        var conflictingParticipantId = await SeedParticipantAsync(competitionId, "Conflicting Owner", conflictingEmail);
        var conflictingEntryId = await SeedBeerEntryAsync(competitionId, conflictingParticipantId, "Own Brew");
        var conflictingJudgeId = await SeedJudgeAsync(competitionId, conflictingEmail);

        // A second, entirely unrelated judge+entry in the same request — proves the rejection
        // is atomic for the whole request, not just the conflicting pair (FR-017: "as a whole").
        var cleanParticipantId = await SeedParticipantAsync(competitionId, "Clean Owner", $"clean-{Guid.NewGuid():N}@brew.example");
        var cleanEntryId = await SeedBeerEntryAsync(competitionId, cleanParticipantId, "Clean Brew");
        var cleanJudgeId = await SeedJudgeAsync(competitionId, $"clean-judge-{Guid.NewGuid():N}@brew.example");

        var response = await CreateTableAsync(
            organizer, competitionId, $"Table {Guid.NewGuid():N}",
            [conflictingJudgeId, cleanJudgeId], [conflictingEntryId, cleanEntryId]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:conflict-of-interest", document.RootElement.GetProperty("type").GetString());

        var conflicts = document.RootElement.GetProperty("conflicts").EnumerateArray().ToList();
        Assert.Contains(conflicts, c =>
            c.GetProperty("judgeId").GetGuid() == conflictingJudgeId
            && c.GetProperty("beerEntryIds").EnumerateArray().Select(e => e.GetGuid()).Contains(conflictingEntryId));

        // Atomic rollback: nothing persisted, not even the non-conflicting judge/entry.
        var listResponse = await GetTablesAsync(organizer, competitionId);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Empty(list.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Create_with_a_collaborator_collision_is_rejected_with_409_conflict_of_interest()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var collaboratorEmail = $"collab-{Guid.NewGuid():N}@brew.example";
        var ownerParticipantId = await SeedParticipantAsync(competitionId, "Owner", $"owner-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(
            competitionId, ownerParticipantId, "Collab Brew", collaboratorEmails: [collaboratorEmail]);
        var judgeId = await SeedJudgeAsync(competitionId, collaboratorEmail);

        var response = await CreateTableAsync(organizer, competitionId, $"Table {Guid.NewGuid():N}", [judgeId], [entryId]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:conflict-of-interest", document.RootElement.GetProperty("type").GetString());
        var conflicts = document.RootElement.GetProperty("conflicts").EnumerateArray().ToList();
        Assert.Contains(conflicts, c => c.GetProperty("judgeId").GetGuid() == judgeId);
    }

    [Fact]
    public async Task Create_with_a_duplicate_table_name_in_the_same_competition_returns_400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var sharedName = $"Table {Guid.NewGuid():N}";

        var firstJudgeId = await SeedJudgeAsync(competitionId, $"first-{Guid.NewGuid():N}@brew.example");
        var firstResponse = await CreateTableAsync(organizer, competitionId, sharedName, [firstJudgeId], []);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondJudgeId = await SeedJudgeAsync(competitionId, $"second-{Guid.NewGuid():N}@brew.example");
        var response = await CreateTableAsync(organizer, competitionId, sharedName, [secondJudgeId], []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_a_beer_entry_already_assigned_to_another_table_returns_400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var participantId = await SeedParticipantAsync(competitionId, "Ana Gomez", $"ana-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Hop Cannon");

        var firstResponse = await CreateTableAsync(organizer, competitionId, $"Table {Guid.NewGuid():N}", [], [entryId]);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var response = await CreateTableAsync(organizer, competitionId, $"Table {Guid.NewGuid():N}", [], [entryId]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- PUT /tables/{tableId}: auth, ownership, closed-table gate ----------------------------

    [Fact]
    public async Task Update_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var createResponse = await CreateTableAsync(organizer, competitionId, $"Table {Guid.NewGuid():N}", [], []);
        var tableId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await UpdateTableAsync(judge, competitionId, tableId, "Renamed", [], []);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_for_a_competition_owned_by_a_different_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var createResponse = await CreateTableAsync(owner, competitionId, $"Table {Guid.NewGuid():N}", [], []);
        var tableId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await UpdateTableAsync(other, competitionId, tableId, "Renamed", [], []);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_on_a_closed_table_returns_409_table_closed()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var tableId = await SeedClosedTableAsync(competitionId, $"Closed {Guid.NewGuid():N}");

        var judgeId = await SeedJudgeAsync(competitionId, $"judge-{Guid.NewGuid():N}@brew.example");
        var response = await UpdateTableAsync(organizer, competitionId, tableId, "Renamed", [judgeId], []);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:table-closed", document.RootElement.GetProperty("type").GetString());
    }

    // ---- PUT /tables/{tableId}: success, re-evaluated COI/BOS ---------------------------------

    [Fact]
    public async Task Update_adds_and_removes_judges_and_entries_in_one_call()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var participant1 = await SeedParticipantAsync(competitionId, "P1", $"p1-{Guid.NewGuid():N}@brew.example");
        var entry1 = await SeedBeerEntryAsync(competitionId, participant1, "Brew 1");
        var participant2 = await SeedParticipantAsync(competitionId, "P2", $"p2-{Guid.NewGuid():N}@brew.example");
        var entry2 = await SeedBeerEntryAsync(competitionId, participant2, "Brew 2");

        var judge1 = await SeedJudgeAsync(competitionId, $"j1-{Guid.NewGuid():N}@brew.example");
        var judge2 = await SeedJudgeAsync(competitionId, $"j2-{Guid.NewGuid():N}@brew.example");

        var tableName = $"Table {Guid.NewGuid():N}";
        var createResponse = await CreateTableAsync(organizer, competitionId, tableName, [judge1], [entry1]);
        var tableId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Full desired-state replace: drop judge1 for judge2, add entry2 alongside entry1.
        var response = await UpdateTableAsync(organizer, competitionId, tableId, tableName, [judge2], [entry1, entry2]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listResponse = await GetTablesAsync(organizer, competitionId);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var table = list.RootElement.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == tableId);

        var judgeIds = ExtractIds(table.GetProperty("judges"), "judgeId", "id");
        Assert.Contains(judge2, judgeIds);
        Assert.DoesNotContain(judge1, judgeIds);

        var sampleIds = ExtractIds(table.GetProperty("samples"), "beerEntryId", "id");
        Assert.Contains(entry1, sampleIds);
        Assert.Contains(entry2, sampleIds);
    }

    [Fact]
    public async Task Update_removing_a_flagged_judges_only_table_lifts_the_bos_flag_before_any_evaluation()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var judgeEmail = $"judge-owner-{Guid.NewGuid():N}@brew.example";
        var judgeAsParticipantId = await SeedParticipantAsync(competitionId, "Judge Brewer", judgeEmail);
        var elsewhereEntryId = await SeedBeerEntryAsync(competitionId, judgeAsParticipantId, "Judge's Own Brew");

        var otherParticipantId = await SeedParticipantAsync(competitionId, "Other Brewer", $"other-{Guid.NewGuid():N}@brew.example");
        var tableEntryId = await SeedBeerEntryAsync(competitionId, otherParticipantId, "Unrelated Brew");

        var flaggedJudgeId = await SeedJudgeAsync(competitionId, judgeEmail);
        var bystanderJudgeId = await SeedJudgeAsync(competitionId, $"bystander-{Guid.NewGuid():N}@brew.example");

        var tableName = $"Table {Guid.NewGuid():N}";
        var createResponse = await CreateTableAsync(
            organizer, competitionId, tableName, [flaggedJudgeId, bystanderJudgeId], [tableEntryId]);
        var tableId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.True(await GetNotValidForBosAsync(elsewhereEntryId));

        // Remove the flagged judge from their only table (bystanderJudgeId stays, so the
        // request body is never an empty judges array — avoids relying on whether the API
        // permits a table with zero judges, which the contract doesn't pin down).
        var response = await UpdateTableAsync(organizer, competitionId, tableId, tableName, [bystanderJudgeId], [tableEntryId]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await GetNotValidForBosAsync(elsewhereEntryId));
    }

    // ---- GET /tables ----------------------------------------------------------------------------

    [Fact]
    public async Task Get_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await GetTablesAsync(judge, competitionId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_for_a_competition_owned_by_a_different_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await GetTablesAsync(other, competitionId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_tables_with_judges_samples_and_state()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var participantId = await SeedParticipantAsync(competitionId, "Ana Gomez", $"ana-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Hop Cannon");
        var judgeId = await SeedJudgeAsync(competitionId, $"judge-{Guid.NewGuid():N}@brew.example");
        var tableName = $"Table {Guid.NewGuid():N}";
        await CreateTableAsync(organizer, competitionId, tableName, [judgeId], [entryId]);

        var response = await GetTablesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var tables = document.RootElement.EnumerateArray().ToList();
        Assert.Single(tables);
        var table = tables[0];
        Assert.Equal(tableName, table.GetProperty("name").GetString());
        Assert.Equal("Open", table.GetProperty("state").GetString());
        Assert.Contains(judgeId, ExtractIds(table.GetProperty("judges"), "judgeId", "id"));
        Assert.Contains(entryId, ExtractIds(table.GetProperty("samples"), "beerEntryId", "id"));
    }
}
