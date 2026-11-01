using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Tables.Features.UpdateTable;

/// <summary>PUT /competitions/{id}/tables/{tableId}: auth/ownership, closed-table and InEvaluation gates, membership diff with re-evaluated COI/BOS (FR-017/FR-018). Live judge removal lives in UpdateTableLiveJudgeRemovalApiTests.</summary>
public sealed class UpdateTableApiTests(ApiFactory factory) : TablesApiTestBase(factory), IClassFixture<ApiFactory>
{
    // ---- PUT /tables/{tableId}: auth, ownership, closed-table gate ----------------------------

    [Fact]
    public async Task UpdateWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var createResponse = await CreateTableAsync(organizer, competitionId, $"Table {Guid.NewGuid():N}", [], []);
        var tableId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await UpdateTableAsync(judge, competitionId, tableId, "Renamed", [], []);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var createResponse = await CreateTableAsync(owner, competitionId, $"Table {Guid.NewGuid():N}", [], []);
        var tableId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await UpdateTableAsync(other, competitionId, tableId, "Renamed", [], []);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateOnAClosedTableReturns409TableClosed()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var tableId = await SeedClosedTableAsync(competitionId, $"Closed {Guid.NewGuid():N}");

        var judgeId = await SeedJudgeAsync(competitionId, $"judge-{Guid.NewGuid():N}@brew.example");
        var response = await UpdateTableAsync(organizer, competitionId, tableId, "Renamed", [judgeId], []);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:table-closed", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UpdateInEvaluationWithANonRemovalChangeReturns409InvalidStateTransition()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var judgeId = await SeedJudgeAsync(competitionId, $"judge-{Guid.NewGuid():N}@brew.example");
        var tableName = $"Table {Guid.NewGuid():N}";
        var createResponse = await CreateTableAsync(organizer, competitionId, tableName, [judgeId], []);
        var tableId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        await organizer.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "Active" }, TestContext.Current.CancellationToken);
        await organizer.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "InEvaluation" }, TestContext.Current.CancellationToken);

        var response = await UpdateTableAsync(organizer, competitionId, tableId, "Renamed live", [judgeId], []);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    // ---- PUT /tables/{tableId}: success, re-evaluated COI/BOS ---------------------------------

    [Fact]
    public async Task UpdateAddsAndRemovesJudgesAndEntriesInOneCall()
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
        var tableId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();

        // Full desired-state replace: drop judge1 for judge2, add entry2 alongside entry1.
        var response = await UpdateTableAsync(organizer, competitionId, tableId, tableName, [judge2], [entry1, entry2]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listResponse = await GetTablesAsync(organizer, competitionId);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var table = list.RootElement.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == tableId);

        var judgeIds = ExtractIds(table.GetProperty("judges"), "judgeId", "id");
        Assert.Contains(judge2, judgeIds);
        Assert.DoesNotContain(judge1, judgeIds);

        var sampleIds = ExtractIds(table.GetProperty("samples"), "beerEntryId", "id");
        Assert.Contains(entry1, sampleIds);
        Assert.Contains(entry2, sampleIds);
    }

    [Fact]
    public async Task UpdateRemovingAFlaggedJudgesOnlyTableLiftsTheBosFlagBeforeAnyEvaluation()
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
        var tableId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        Assert.True(await GetNotValidForBosAsync(elsewhereEntryId));

        // Remove the flagged judge from their only table (bystanderJudgeId stays, so the
        // request body is never an empty judges array — avoids relying on whether the API
        // permits a table with zero judges, which the contract doesn't pin down).
        var response = await UpdateTableAsync(organizer, competitionId, tableId, tableName, [bystanderJudgeId], [tableEntryId]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await GetNotValidForBosAsync(elsewhereEntryId));
    }

    [Fact]
    public async Task UpdateRemovingOneCoOwnerDoesNotLiftTheBosFlagWhileAnotherCoOwnerStaysActive()
    {
        // senior-code-reviewer PR #19 finding: the unflag check originally looked only at the
        // judge leaving THIS table, ignoring a co-owner (collaborator) still seated at a
        // different table. A shared entry must stay flagged as long as ANY owner/collaborator
        // judge remains actively assigned anywhere in the competition.
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var ownerEmail = $"owner-{Guid.NewGuid():N}@brew.example";
        var collaboratorEmail = $"collaborator-{Guid.NewGuid():N}@brew.example";
        var ownerParticipantId = await SeedParticipantAsync(competitionId, "Shared Brewer", ownerEmail);
        var sharedEntryId = await SeedBeerEntryAsync(
            competitionId, ownerParticipantId, "Shared Brew", collaboratorEmails: [collaboratorEmail]);

        var otherParticipantId = await SeedParticipantAsync(competitionId, "Other Brewer", $"other-{Guid.NewGuid():N}@brew.example");
        var table1EntryId = await SeedBeerEntryAsync(competitionId, otherParticipantId, "Table 1 Brew");
        var table2EntryId = await SeedBeerEntryAsync(competitionId, otherParticipantId, "Table 2 Brew");

        var ownerJudgeId = await SeedJudgeAsync(competitionId, ownerEmail);
        var collaboratorJudgeId = await SeedJudgeAsync(competitionId, collaboratorEmail);
        var bystanderJudgeId = await SeedJudgeAsync(competitionId, $"bystander-{Guid.NewGuid():N}@brew.example");

        var table1Name = $"Table1 {Guid.NewGuid():N}";
        var table1Response = await CreateTableAsync(
            organizer, competitionId, table1Name, [ownerJudgeId, bystanderJudgeId], [table1EntryId]);
        var table1Id = (await table1Response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();

        var table2Name = $"Table2 {Guid.NewGuid():N}";
        await CreateTableAsync(organizer, competitionId, table2Name, [collaboratorJudgeId], [table2EntryId]);

        Assert.True(await GetNotValidForBosAsync(sharedEntryId));

        // Remove the owner-judge from Table 1 — the collaborator-judge is still seated at
        // Table 2, so the shared entry must stay flagged.
        var response = await UpdateTableAsync(organizer, competitionId, table1Id, table1Name, [bystanderJudgeId], [table1EntryId]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await GetNotValidForBosAsync(sharedEntryId));
    }
}
