using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Judging.Integration;

/// <summary>IEntryTableAssignments (implemented by Judging): which table each entry sits at.</summary>
public sealed class EntryTableAssignmentsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly ContractSeeder _seed = new(factory.Services);

    private static IEntryTableAssignments Assignments(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IEntryTableAssignments>();

    [Fact]
    public async Task GetByEntryReturnsTheTableOfAssignedEntriesAndOmitsUnassignedOnes()
    {
        var competition = await _seed.CompetitionAsync();
        var table = await _seed.TableAsync(competition, "Table Alpha");
        var assigned = await _seed.EntryAsync(competition, $"a-{Guid.NewGuid():N}@brew.example");
        var unassigned = await _seed.EntryAsync(competition, $"u-{Guid.NewGuid():N}@brew.example");
        await _seed.SampleAsync(table, assigned);
        using var scope = factory.Services.CreateScope();

        var result = await Assignments(scope).GetByEntryAsync(competition, [assigned, unassigned], TestContext.Current.CancellationToken);

        var assignment = Assert.Single(result);
        Assert.Equal(assigned, assignment.Key);
        Assert.Equal(new EntryTableAssignment(table, "Table Alpha"), assignment.Value);
    }

    [Fact]
    public async Task GetByEntryIsScopedToTheGivenCompetition()
    {
        var competition = await _seed.CompetitionAsync();
        var other = await _seed.CompetitionAsync();
        var otherTable = await _seed.TableAsync(other, "Foreign");
        var otherEntry = await _seed.EntryAsync(other, $"o-{Guid.NewGuid():N}@brew.example");
        await _seed.SampleAsync(otherTable, otherEntry);
        using var scope = factory.Services.CreateScope();

        var result = await Assignments(scope).GetByEntryAsync(competition, [otherEntry], TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByEntryWithNoEntriesReturnsEmpty()
    {
        var competition = await _seed.CompetitionAsync();
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Assignments(scope).GetByEntryAsync(competition, [], TestContext.Current.CancellationToken));
    }
}
