using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Integration;

/// <summary>ICompetitionAccess (ADR-0034): the read surface other modules use for ownership scoping,
/// state gates and batch state lookups, without reaching into the Organization tables.</summary>
public sealed class CompetitionAccessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static ICompetitionAccess Access(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ICompetitionAccess>();

    private async Task<(Guid Id, string Name, string Owner)> SeedAsync(CompetitionState state = CompetitionState.Draft)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var competition = new Competition
        {
            Name = $"Access {Guid.NewGuid():N}",
            Venue = "Venue",
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 3),
            CreatedByUserId = $"organizer-{Guid.NewGuid():N}",
            State = state,
        };
        db.Competitions.Add(competition);
        await db.SaveChangesAsync();
        return (competition.Id, competition.Name, competition.CreatedByUserId);
    }

    [Fact]
    public async Task GetOwnedReturnsTheSnapshotForTheOwner()
    {
        var (id, name, owner) = await SeedAsync(CompetitionState.Active);
        using var scope = factory.Services.CreateScope();

        var snapshot = await Access(scope).GetOwnedAsync(id, owner, TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(id, snapshot.Id);
        Assert.Equal(name, snapshot.Name);
        Assert.Equal(CompetitionState.Active, snapshot.State);
    }

    [Fact]
    public async Task GetOwnedReturnsNullForAnotherOrganizer()
    {
        var (id, _, _) = await SeedAsync();
        using var scope = factory.Services.CreateScope();

        Assert.Null(await Access(scope).GetOwnedAsync(id, $"someone-else-{Guid.NewGuid():N}", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetOwnedReturnsNullForAnUnknownCompetition()
    {
        using var scope = factory.Services.CreateScope();

        Assert.Null(await Access(scope).GetOwnedAsync(Guid.NewGuid(), "organizer-any", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetReturnsNameAndStateWithoutAnOwnershipCheck()
    {
        var (id, name, _) = await SeedAsync();
        using var scope = factory.Services.CreateScope();

        var snapshot = await Access(scope).GetAsync(id, TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(id, snapshot.Id);
        Assert.Equal(name, snapshot.Name);
        Assert.Equal(CompetitionState.Draft, snapshot.State);
    }

    [Fact]
    public async Task GetReturnsNullForAnUnknownCompetition()
    {
        using var scope = factory.Services.CreateScope();

        Assert.Null(await Access(scope).GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetReflectsANonDraftState()
    {
        var (id, _, _) = await SeedAsync(CompetitionState.InEvaluation);
        using var scope = factory.Services.CreateScope();

        var snapshot = await Access(scope).GetAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(CompetitionState.InEvaluation, snapshot?.State);
    }

    [Fact]
    public async Task GetStatesWithEmptyInputReturnsAnEmptyDictionary()
    {
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Access(scope).GetStatesAsync([], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetStatesReturnsOnlyKnownIdsWithTheirStates()
    {
        var (draft, _, _) = await SeedAsync();
        var (finalized, _, _) = await SeedAsync(CompetitionState.Finalized);
        var unknown = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();

        var states = await Access(scope).GetStatesAsync([draft, finalized, unknown], TestContext.Current.CancellationToken);

        Assert.Equal(2, states.Count);
        Assert.Equal(CompetitionState.Draft, states[draft]);
        Assert.Equal(CompetitionState.Finalized, states[finalized]);
        Assert.False(states.ContainsKey(unknown));
    }

    [Fact]
    public async Task GetStatesToleratesDuplicateIds()
    {
        var (id, _, _) = await SeedAsync(CompetitionState.Active);
        using var scope = factory.Services.CreateScope();

        var states = await Access(scope).GetStatesAsync([id, id, id], TestContext.Current.CancellationToken);

        var entry = Assert.Single(states);
        Assert.Equal(id, entry.Key);
        Assert.Equal(CompetitionState.Active, entry.Value);
    }
}
