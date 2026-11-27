using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Judging.Integration;

/// <summary>IJudgeEngagement (owned by Organization, implemented by Judging): a judge stays engaged, and
/// keeps the entries they own out of BOS (FR-018), while seated at any table of the competition or once they
/// have submitted any evaluation.</summary>
public sealed class JudgeEngagementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly ContractSeeder _seed = new(factory.Services);

    private static IJudgeEngagement Engagement(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IJudgeEngagement>();

    private static string Email() => $"judge-{Guid.NewGuid():N}@judges.example";

    [Fact]
    public async Task JudgeSeatedAtATableIsEngaged()
    {
        var competition = await _seed.CompetitionAsync();
        var judge = await _seed.JudgeAsync(competition, Email());
        var idle = await _seed.JudgeAsync(competition, Email());
        var table = await _seed.TableAsync(competition, "Table");
        await _seed.TableJudgeAsync(table, judge);
        using var scope = factory.Services.CreateScope();

        var engaged = await Engagement(scope).GetEngagedJudgesAsync(
            competition, [judge, idle], TestContext.Current.CancellationToken);

        Assert.Equal([judge], engaged);
    }

    [Fact]
    public async Task RemovedSeatDoesNotCountAsEngagement()
    {
        var competition = await _seed.CompetitionAsync();
        var judge = await _seed.JudgeAsync(competition, Email());
        var table = await _seed.TableAsync(competition, "Table");
        await _seed.TableJudgeAsync(table, judge, removed: true);
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Engagement(scope).GetEngagedJudgesAsync(
            competition, [judge], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task JudgeWithAnySubmittedEvaluationIsEngaged()
    {
        var competition = await _seed.CompetitionAsync();
        var judge = await _seed.JudgeAsync(competition, Email());
        var idle = await _seed.JudgeAsync(competition, Email());
        var table = await _seed.TableAsync(competition, "Table");
        var entry = await _seed.EntryAsync(competition, $"entrant-{Guid.NewGuid():N}@brew.example");
        await _seed.SampleAsync(table, entry);
        await _seed.EvaluationAsync(table, judge, entry);
        using var scope = factory.Services.CreateScope();

        var engaged = await Engagement(scope).GetEngagedJudgesAsync(
            competition, [judge, idle], TestContext.Current.CancellationToken);

        Assert.Equal([judge], engaged);
    }

    [Fact]
    public async Task EmptyInputReturnsEmpty()
    {
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Engagement(scope).GetEngagedJudgesAsync(
            Guid.NewGuid(), [], TestContext.Current.CancellationToken));
    }
}
