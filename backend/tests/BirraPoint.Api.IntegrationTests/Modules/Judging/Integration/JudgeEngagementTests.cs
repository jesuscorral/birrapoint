using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Judging.Integration;

/// <summary>IJudgeEngagement (owned by Organization, implemented by Judging): a judge stays engaged, and
/// keeps the entries they own out of BOS (FR-018), while seated at another table or once they have
/// submitted any evaluation.</summary>
public sealed class JudgeEngagementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly ContractSeeder _seed = new(factory.Services);

    private static IJudgeEngagement Engagement(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IJudgeEngagement>();

    private static string Email() => $"judge-{Guid.NewGuid():N}@judges.example";

    [Fact]
    public async Task JudgeSeatedAtAnotherTableIsEngaged()
    {
        var competition = await _seed.CompetitionAsync();
        var judge = await _seed.JudgeAsync(competition, Email());
        var excludedTable = await _seed.TableAsync(competition, "Excluded");
        var otherTable = await _seed.TableAsync(competition, "Other");
        await _seed.TableJudgeAsync(excludedTable, judge);
        await _seed.TableJudgeAsync(otherTable, judge);
        using var scope = factory.Services.CreateScope();

        var engaged = await Engagement(scope).GetJudgesEngagedOutsideTableAsync(
            excludedTable, [judge], TestContext.Current.CancellationToken);

        Assert.Equal([judge], engaged);
    }

    [Fact]
    public async Task JudgeSeatedOnlyAtTheExcludedTableIsNotEngaged()
    {
        var competition = await _seed.CompetitionAsync();
        var judge = await _seed.JudgeAsync(competition, Email());
        var excludedTable = await _seed.TableAsync(competition, "Excluded");
        await _seed.TableJudgeAsync(excludedTable, judge);
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Engagement(scope).GetJudgesEngagedOutsideTableAsync(
            excludedTable, [judge], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemovedSeatDoesNotCountAsEngagement()
    {
        var competition = await _seed.CompetitionAsync();
        var judge = await _seed.JudgeAsync(competition, Email());
        var excludedTable = await _seed.TableAsync(competition, "Excluded");
        var otherTable = await _seed.TableAsync(competition, "Other");
        await _seed.TableJudgeAsync(otherTable, judge, removed: true);
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Engagement(scope).GetJudgesEngagedOutsideTableAsync(
            excludedTable, [judge], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task JudgeWithAnySubmittedEvaluationIsEngagedEvenAtTheExcludedTable()
    {
        var competition = await _seed.CompetitionAsync();
        var judge = await _seed.JudgeAsync(competition, Email());
        var idle = await _seed.JudgeAsync(competition, Email());
        var table = await _seed.TableAsync(competition, "Table");
        var entry = await _seed.EntryAsync(competition, $"entrant-{Guid.NewGuid():N}@brew.example");
        await _seed.SampleAsync(table, entry);
        await _seed.EvaluationAsync(table, judge, entry);
        using var scope = factory.Services.CreateScope();

        var engaged = await Engagement(scope).GetJudgesEngagedOutsideTableAsync(
            table, [judge, idle], TestContext.Current.CancellationToken);

        Assert.Equal([judge], engaged);
    }

    [Fact]
    public async Task EmptyInputReturnsEmpty()
    {
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Engagement(scope).GetJudgesEngagedOutsideTableAsync(
            Guid.NewGuid(), [], TestContext.Current.CancellationToken));
    }
}
