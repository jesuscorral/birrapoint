using System.Net;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.Modules.Judging.TastingOrder.Features.GetMyTables;

/// <summary>
/// HTTP-level contract tests for GET /me/tables (contracts/rest-api.md §Judge workspace) —
/// active-membership scoping and Active+ competition visibility.
/// </summary>
public sealed class GetMyTablesApiTests(ApiFactory factory) : TastingOrderApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task GetMyTablesReturnsEmptyListForAJudgeWithNoAssignments()
    {
        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");

        var response = await GetMyTablesAsync(judge);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(document.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task GetMyTablesReturnsOnlyTablesTheJudgeIsActivelyAssignedTo()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (competitionId, tableId, _, judgeSub, _) = await SeedActiveTableWithSamplesAsync(organizer);

        // An unrelated table in the same competition the judge is NOT assigned to.
        await SeedTableAsync(competitionId, $"Other {Guid.NewGuid():N}");

        using var judge = JudgeClient(judgeSub);
        var response = await GetMyTablesAsync(judge);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var tables = document.RootElement.EnumerateArray().ToList();
        var table = Assert.Single(tables);
        Assert.Equal(tableId, table.GetProperty("tableId").GetGuid());
        Assert.False(table.GetProperty("orderFixed").GetBoolean());
    }

    [Fact]
    public async Task GetMyTablesExcludesTablesWhereTheJudgeWasRemoved()
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
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(document.RootElement.EnumerateArray());
    }

    /// <summary>contracts/rest-api.md §Judge workspace: "Competition must be Active+ (invisible
    /// in Draft)".</summary>
    [Fact]
    public async Task GetMyTablesHidesTablesOfACompetitionStillInDraft()
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
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(document.RootElement.EnumerateArray());
    }
}
