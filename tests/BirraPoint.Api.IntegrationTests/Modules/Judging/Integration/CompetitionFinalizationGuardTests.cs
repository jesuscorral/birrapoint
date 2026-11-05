using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Judging.Integration;

/// <summary>ICompetitionFinalizationGuard (implemented by Judging): lists the competition's tables that
/// are not closed yet.</summary>
public sealed class CompetitionFinalizationGuardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly ContractSeeder _seed = new(factory.Services);

    [Fact]
    public async Task GetOpenTableIdsReturnsOnlyTheTablesThatAreNotClosed()
    {
        var competition = await _seed.CompetitionAsync();
        var open = await _seed.TableAsync(competition, "Open", TableState.Open);
        await _seed.TableAsync(competition, "Closed", TableState.Closed);
        using var scope = factory.Services.CreateScope();

        var ids = await scope.ServiceProvider.GetRequiredService<ICompetitionFinalizationGuard>()
            .GetOpenTableIdsAsync(competition, TestContext.Current.CancellationToken);

        Assert.Equal([open], ids);
    }

    [Fact]
    public async Task GetOpenTableIdsIsEmptyWhenEveryTableIsClosedOrThereAreNone()
    {
        var closedOnly = await _seed.CompetitionAsync();
        await _seed.TableAsync(closedOnly, "Closed", TableState.Closed);
        var noTables = await _seed.CompetitionAsync();
        using var scope = factory.Services.CreateScope();
        var guard = scope.ServiceProvider.GetRequiredService<ICompetitionFinalizationGuard>();

        Assert.Empty(await guard.GetOpenTableIdsAsync(closedOnly, TestContext.Current.CancellationToken));
        Assert.Empty(await guard.GetOpenTableIdsAsync(noTables, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetOpenTableIdsNeverIncludesAnotherCompetitionsTables()
    {
        var competition = await _seed.CompetitionAsync();
        var other = await _seed.CompetitionAsync();
        await _seed.TableAsync(other, "Foreign open", TableState.Open);
        using var scope = factory.Services.CreateScope();

        var ids = await scope.ServiceProvider.GetRequiredService<ICompetitionFinalizationGuard>()
            .GetOpenTableIdsAsync(competition, TestContext.Current.CancellationToken);

        Assert.Empty(ids);
    }
}
