using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Integration;

/// <summary>IJudgeDirectory (ADR-0033 phase 3): the read surface Judging uses for judge names, ranks and
/// competition membership, without reaching into the Organization judge table.</summary>
public sealed class JudgeDirectoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly ContractSeeder _seed = new(factory.Services);

    private static IJudgeDirectory Directory(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IJudgeDirectory>();

    private static string Email(string prefix) => $"{prefix}-{Guid.NewGuid():N}@judges.example";

    [Fact]
    public async Task GetDisplayNamesReturnsKnownJudgesOnly()
    {
        var competition = await _seed.CompetitionAsync();
        var judge = await _seed.JudgeAsync(competition, Email("names"), "Ana Names");
        using var scope = factory.Services.CreateScope();

        var names = await Directory(scope).GetDisplayNamesAsync([judge, Guid.NewGuid()], TestContext.Current.CancellationToken);

        var name = Assert.Single(names);
        Assert.Equal(judge, name.Key);
        Assert.Equal("Ana Names", name.Value);
    }

    [Fact]
    public async Task GetSummariesReturnsCompetitionEmailNameAndRankOrderedByDisplayName()
    {
        var competition = await _seed.CompetitionAsync();
        var zoeEmail = Email("zoe");
        var zoe = await _seed.JudgeAsync(competition, zoeEmail, "Zoe", bjcpRank: "National");
        var alvaro = await _seed.JudgeAsync(competition, Email("alvaro"), "Álvaro");
        var bruno = await _seed.JudgeAsync(competition, Email("bruno"), "Bruno");
        using var scope = factory.Services.CreateScope();

        var summaries = await Directory(scope).GetSummariesAsync([zoe, bruno, alvaro, Guid.NewGuid()], TestContext.Current.CancellationToken);

        Assert.Equal([alvaro, bruno, zoe], summaries.Select(s => s.Id));
        var zoeSummary = summaries[2];
        Assert.Equal(competition, zoeSummary.CompetitionId);
        Assert.Equal(zoeEmail, zoeSummary.Email);
        Assert.Equal("Zoe", zoeSummary.DisplayName);
        Assert.Equal("National", zoeSummary.BjcpRank);
        Assert.Null(summaries[0].BjcpRank);
    }

    [Fact]
    public async Task GetSummariesWithEmptyInputReturnsEmpty()
    {
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Directory(scope).GetSummariesAsync([], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetCompetitionJudgeIdsKeepsOnlyJudgesOfThatCompetition()
    {
        var competition = await _seed.CompetitionAsync();
        var otherCompetition = await _seed.CompetitionAsync();
        var member = await _seed.JudgeAsync(competition, Email("member"));
        var outsider = await _seed.JudgeAsync(otherCompetition, Email("outsider"));
        using var scope = factory.Services.CreateScope();

        var ids = await Directory(scope).GetCompetitionJudgeIdsAsync(
            competition, [member, member, outsider, Guid.NewGuid()], TestContext.Current.CancellationToken);

        Assert.Equal([member], ids);
    }

    [Fact]
    public async Task GetCompetitionJudgeIdsWithEmptyInputReturnsEmpty()
    {
        var competition = await _seed.CompetitionAsync();
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Directory(scope).GetCompetitionJudgeIdsAsync(competition, [], TestContext.Current.CancellationToken));
    }
}
