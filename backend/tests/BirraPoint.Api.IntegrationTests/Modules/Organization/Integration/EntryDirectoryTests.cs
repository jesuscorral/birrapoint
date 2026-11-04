using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Integration;

/// <summary>IEntryDirectory (ADR-0033 phase 3): the anonymous entry read surface Judging uses for samples
/// (BR-01/FR-019), without reaching into the Organization entry tables.</summary>
public sealed class EntryDirectoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly ContractSeeder _seed = new(factory.Services);

    private static IEntryDirectory Directory(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IEntryDirectory>();

    private static string Email() => $"entrant-{Guid.NewGuid():N}@brew.example";

    [Fact]
    public async Task GetSamplesReturnsTheAnonymousSampleData()
    {
        var competition = await _seed.CompetitionAsync();
        var category = await _seed.CategoryAsync(competition, "Belgian ales");
        var entry = await _seed.EntryAsync(competition, Email(), configure: e =>
        {
            e.StyleCode = "25B";
            e.AbvPercent = 6.4m;
            e.EntryInstructions = "Serve cold";
            e.NotValidForBos = true;
            e.CompetitionCategoryId = category;
        });
        using var scope = factory.Services.CreateScope();

        var samples = await Directory(scope).GetSamplesAsync([entry, Guid.NewGuid()], TestContext.Current.CancellationToken);

        var sample = Assert.Single(samples).Value;
        Assert.Equal(entry, sample.Id);
        Assert.Equal(competition, sample.CompetitionId);
        Assert.False(string.IsNullOrEmpty(sample.BlindCode));
        Assert.Equal("25B", sample.StyleCode);
        Assert.Equal(6.4m, sample.AbvPercent);
        Assert.Equal("Serve cold", sample.EntryInstructions);
        Assert.True(sample.NotValidForBos);
        Assert.Equal("Belgian ales", sample.CompetitionCategoryName);
    }

    [Fact]
    public async Task GetSamplesLeavesTheCategoryNullWhenTheEntryHasNone()
    {
        var competition = await _seed.CompetitionAsync();
        var entry = await _seed.EntryAsync(competition, Email());
        using var scope = factory.Services.CreateScope();

        var samples = await Directory(scope).GetSamplesAsync([entry, entry], TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(samples).Value.CompetitionCategoryName);
    }

    [Fact]
    public async Task GetSamplesWithEmptyInputReturnsEmpty()
    {
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Directory(scope).GetSamplesAsync([], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetCompetitionEntryIdsKeepsOnlyEntriesOfThatCompetition()
    {
        var competition = await _seed.CompetitionAsync();
        var otherCompetition = await _seed.CompetitionAsync();
        var member = await _seed.EntryAsync(competition, Email());
        var outsider = await _seed.EntryAsync(otherCompetition, Email());
        using var scope = factory.Services.CreateScope();

        var ids = await Directory(scope).GetCompetitionEntryIdsAsync(
            competition, [member, member, outsider, Guid.NewGuid()], TestContext.Current.CancellationToken);

        Assert.Equal([member], ids);
    }

    [Fact]
    public async Task GetCompetitionEntryIdsWithEmptyInputReturnsEmpty()
    {
        var competition = await _seed.CompetitionAsync();
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Directory(scope).GetCompetitionEntryIdsAsync(competition, [], TestContext.Current.CancellationToken));
    }
}
