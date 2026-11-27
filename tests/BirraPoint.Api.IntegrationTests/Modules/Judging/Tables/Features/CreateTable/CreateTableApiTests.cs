using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Judging.Tables.Features.CreateTable;

/// <summary>POST /competitions/{id}/tables: auth/ownership, conflict-of-interest rejection with atomic rollback (FR-017), BOS flagging (FR-018), name and entry uniqueness.</summary>
public sealed class CreateTableApiTests(ApiFactory factory) : TablesApiTestBase(factory), IClassFixture<ApiFactory>
{
    // ---- POST /tables: auth & ownership -------------------------------------------------------

    [Fact]
    public async Task CreateWithoutABearerTokenIsRejectedWith401()
    {
        using var client = Factory.CreateClient();

        var response = await CreateTableAsync(client, Guid.NewGuid(), "Table 1", [], []);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await CreateTableAsync(judge, competitionId, "Table 1", [], []);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateForACompetitionOwnedByADifferentOrganizerReturns404()
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
    public async Task CreateWithNoConflictsSucceedsAndPersistsJudgesAndEntries()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var participantId = await SeedParticipantAsync(competitionId, "Ana Gomez", $"ana-{Guid.NewGuid():N}@brew.example");
        var entryId = await SeedBeerEntryAsync(competitionId, participantId, "Hop Cannon");
        var judgeId = await SeedJudgeAsync(competitionId, $"judge-{Guid.NewGuid():N}@brew.example");
        var tableName = $"Table {Guid.NewGuid():N}";

        var response = await CreateTableAsync(organizer, competitionId, tableName, [judgeId], [entryId]);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.NotEqual(Guid.Empty, created.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(tableName, created.RootElement.GetProperty("name").GetString());
        Assert.Equal("Open", created.RootElement.GetProperty("state").GetString());

        // No FR-018 trigger here — the judge has no entries of their own in the competition.
        if (created.RootElement.TryGetProperty("bosFlaggedEntryIds", out var bosFlagged))
        {
            Assert.Empty(bosFlagged.EnumerateArray());
        }

        var listResponse = await GetTablesAsync(organizer, competitionId);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var tables = list.RootElement.EnumerateArray().ToList();
        Assert.Single(tables);
        var table = tables[0];
        Assert.Equal(tableName, table.GetProperty("name").GetString());
        Assert.Contains(judgeId, ExtractIds(table.GetProperty("judges"), "judgeId", "id"));
        Assert.Contains(entryId, ExtractIds(table.GetProperty("samples"), "beerEntryId", "id"));
    }

    [Fact]
    public async Task CreateFlagsAllOfTheJudgesEntriesAsNotValidForBosWithoutADirectCollision()
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
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var bosFlagged = created.RootElement.GetProperty("bosFlaggedEntryIds").EnumerateArray()
            .Select(e => e.GetGuid()).ToHashSet();
        Assert.Contains(elsewhereEntryId, bosFlagged);
        Assert.DoesNotContain(tableEntryId, bosFlagged); // no direct collision, so it wasn't rejected

        await AssertNotValidForBosEventuallyAsync(elsewhereEntryId, expected: true);
    }

    [Fact]
    public async Task CreateWithADirectOwnerCollisionIsRejectedWith409AndNothingPersisted()
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
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:conflict-of-interest", document.RootElement.GetProperty("type").GetString());

        var conflicts = document.RootElement.GetProperty("conflicts").EnumerateArray().ToList();
        Assert.Contains(conflicts, c =>
            c.GetProperty("judgeId").GetGuid() == conflictingJudgeId
            && c.GetProperty("beerEntryIds").EnumerateArray().Select(e => e.GetGuid()).Contains(conflictingEntryId));

        // Atomic rollback: nothing persisted, not even the non-conflicting judge/entry.
        var listResponse = await GetTablesAsync(organizer, competitionId);
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(list.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task CreateWithACollaboratorCollisionIsRejectedWith409ConflictOfInterest()
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
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:conflict-of-interest", document.RootElement.GetProperty("type").GetString());
        var conflicts = document.RootElement.GetProperty("conflicts").EnumerateArray().ToList();
        Assert.Contains(conflicts, c => c.GetProperty("judgeId").GetGuid() == judgeId);
    }

    [Fact]
    public async Task CreateWithADuplicateTableNameInTheSameCompetitionReturns400()
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
    public async Task CreateWithADuplicateTableNameDifferingOnlyInCaseReturns400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var name = $"Table {Guid.NewGuid():N}";

        var firstResponse = await CreateTableAsync(organizer, competitionId, name, [], []);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var response = await CreateTableAsync(organizer, competitionId, name.ToUpperInvariant(), [], []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateWithLikeWildcardsInTheNameMatchesThemLiterally()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var suffix = Guid.NewGuid().ToString("N");

        var firstResponse = await CreateTableAsync(organizer, competitionId, $"Table A{suffix}", [], []);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        // "_" and "%" must not act as LIKE wildcards: neither name collides with "Table A{suffix}".
        var underscoreResponse = await CreateTableAsync(organizer, competitionId, $"Table _{suffix}", [], []);
        var percentResponse = await CreateTableAsync(organizer, competitionId, "Table %", [], []);

        Assert.Equal(HttpStatusCode.Created, underscoreResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, percentResponse.StatusCode);
    }

    [Fact]
    public async Task CreateWithABeerEntryAlreadyAssignedToAnotherTableReturns400()
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
}
