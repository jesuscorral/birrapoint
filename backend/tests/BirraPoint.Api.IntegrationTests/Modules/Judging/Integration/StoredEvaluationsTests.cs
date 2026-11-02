using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Judging.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Judging.Integration;

/// <summary>IStoredEvaluations: evaluations per entry for score sheets and the audit drill-down.</summary>
public sealed class StoredEvaluationsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string DescriptorsJson =
        "{\"aroma\":{\"malt\":2,\"maltInappropriate\":false,\"hops\":1,\"hopsInappropriate\":true,\"fermentation\":null,\"fermentationInappropriate\":false},\"offFlavors\":[\"diacetyl\"]}";

    private readonly ContractSeeder _seed = new(factory.Services);

    private static IStoredEvaluations Evaluations(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IStoredEvaluations>();

    [Fact]
    public async Task GetByEntriesReturnsEvaluationsWithJudgeNameScoresAndStatus()
    {
        var competition = await _seed.CompetitionAsync();
        var table = await _seed.TableAsync(competition, "T1");
        var judge = await _seed.JudgeAsync(competition, $"j-{Guid.NewGuid():N}@brew.example", "Ana Judge");
        var entry = await _seed.EntryAsync(competition, $"p-{Guid.NewGuid():N}@brew.example");
        await _seed.EvaluationAsync(table, judge, entry, feedback: "Nice work");
        using var scope = factory.Services.CreateScope();

        var result = await Evaluations(scope).GetByEntriesAsync([entry], TestContext.Current.CancellationToken);

        var evaluation = Assert.Single(result[entry]);
        Assert.Equal(entry, evaluation.BeerEntryId);
        Assert.Equal("Ana Judge", evaluation.JudgeDisplayName);
        Assert.Equal(10, evaluation.AromaScore);
        Assert.Equal(39, evaluation.Total);
        Assert.Equal("Confirmed", evaluation.Status);
        Assert.Equal("Nice work", evaluation.Feedback);
        Assert.Null(evaluation.Descriptors);
    }

    [Fact]
    public async Task GetByEntriesMapsEntriesWithoutEvaluationsToAnEmptyList()
    {
        var competition = await _seed.CompetitionAsync();
        var entry = await _seed.EntryAsync(competition, $"p-{Guid.NewGuid():N}@brew.example");
        using var scope = factory.Services.CreateScope();

        var result = await Evaluations(scope).GetByEntriesAsync([entry], TestContext.Current.CancellationToken);

        Assert.Empty(result[entry]);
    }

    [Fact]
    public async Task GetByEntriesWithNoEntriesReturnsEmpty()
    {
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Evaluations(scope).GetByEntriesAsync([], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetByEntriesDeserializesStoredDescriptors()
    {
        var competition = await _seed.CompetitionAsync();
        var table = await _seed.TableAsync(competition, "T1");
        var judge = await _seed.JudgeAsync(competition, $"j-{Guid.NewGuid():N}@brew.example");
        var entry = await _seed.EntryAsync(competition, $"p-{Guid.NewGuid():N}@brew.example");
        await _seed.EvaluationAsync(table, judge, entry, descriptorsJson: DescriptorsJson);
        using var scope = factory.Services.CreateScope();

        var result = await Evaluations(scope).GetByEntriesAsync([entry], TestContext.Current.CancellationToken);

        var descriptors = Assert.Single(result[entry]).Descriptors;
        Assert.NotNull(descriptors);
        Assert.Equal(2, descriptors.Aroma?.Malt);
        Assert.True(descriptors.Aroma?.HopsInappropriate);
        Assert.Null(descriptors.Aroma?.Fermentation);
        Assert.Equal(["diacetyl"], descriptors.OffFlavors);
        Assert.Null(descriptors.Flavor);
    }

    [Fact]
    public async Task GetByEntriesGroupsEvaluationsPerEntryAndNeverMixesEntries()
    {
        var competition = await _seed.CompetitionAsync();
        var table = await _seed.TableAsync(competition, "T1");
        var judgeA = await _seed.JudgeAsync(competition, $"a-{Guid.NewGuid():N}@brew.example");
        var judgeB = await _seed.JudgeAsync(competition, $"b-{Guid.NewGuid():N}@brew.example");
        var first = await _seed.EntryAsync(competition, $"p1-{Guid.NewGuid():N}@brew.example");
        var second = await _seed.EntryAsync(competition, $"p2-{Guid.NewGuid():N}@brew.example");
        var notRequested = await _seed.EntryAsync(competition, $"p3-{Guid.NewGuid():N}@brew.example");
        await _seed.EvaluationAsync(table, judgeA, first);
        await _seed.EvaluationAsync(table, judgeB, first);
        await _seed.EvaluationAsync(table, judgeA, second);
        await _seed.EvaluationAsync(table, judgeA, notRequested);
        using var scope = factory.Services.CreateScope();

        var result = await Evaluations(scope).GetByEntriesAsync([first, second], TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[first].Count);
        Assert.All(result[first], e => Assert.Equal(first, e.BeerEntryId));
        Assert.Single(result[second]);
        Assert.False(result.ContainsKey(notRequested));
    }
}
