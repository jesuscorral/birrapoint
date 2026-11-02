using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Integration;

/// <summary>IConflictOfInterestChecker (FR-018): a judge conflicts with an entry when the judge's e-mail
/// equals, case-insensitively, the participant's or a collaborator's e-mail, within one competition.</summary>
public sealed class ConflictOfInterestCheckerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly ContractSeeder _seed = new(factory.Services);

    private static IConflictOfInterestChecker Checker(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IConflictOfInterestChecker>();

    [Fact]
    public async Task FindConflictsReturnsTheJudgeThatIsTheEntryParticipant()
    {
        var competition = await _seed.CompetitionAsync();
        var ownerEmail = $"owner-{Guid.NewGuid():N}@brew.example";
        var judge = await _seed.JudgeAsync(competition, ownerEmail);
        var other = await _seed.JudgeAsync(competition, $"other-{Guid.NewGuid():N}@brew.example");
        var entry = await _seed.EntryAsync(competition, ownerEmail);
        using var scope = factory.Services.CreateScope();

        var conflicts = await Checker(scope).FindConflictsAsync(competition, [judge, other], [entry], TestContext.Current.CancellationToken);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(judge, conflict.JudgeId);
        Assert.Equal([entry], conflict.BeerEntryIds);
    }

    [Fact]
    public async Task FindConflictsMatchesEmailsCaseInsensitively()
    {
        var competition = await _seed.CompetitionAsync();
        var local = $"mixed-{Guid.NewGuid():N}";
        var judge = await _seed.JudgeAsync(competition, $"{local}@Brew.Example");
        var entry = await _seed.EntryAsync(competition, $"{local.ToUpperInvariant()}@BREW.EXAMPLE");
        using var scope = factory.Services.CreateScope();

        var conflicts = await Checker(scope).FindConflictsAsync(competition, [judge], [entry], TestContext.Current.CancellationToken);

        Assert.Single(conflicts, c => c.JudgeId == judge);
    }

    [Fact]
    public async Task FindConflictsMatchesCollaboratorEmails()
    {
        var competition = await _seed.CompetitionAsync();
        var email = $"collab-{Guid.NewGuid():N}@brew.example";
        var judge = await _seed.JudgeAsync(competition, email);
        var entry = await _seed.EntryAsync(competition, $"someone-{Guid.NewGuid():N}@brew.example", [email.ToUpperInvariant()]);
        using var scope = factory.Services.CreateScope();

        var conflicts = await Checker(scope).FindConflictsAsync(competition, [judge], [entry], TestContext.Current.CancellationToken);

        var conflict = Assert.Single(conflicts);
        Assert.Equal([entry], conflict.BeerEntryIds);
    }

    [Fact]
    public async Task FindConflictsReturnsEmptyWhenNoJudgeOwnsTheEntries()
    {
        var competition = await _seed.CompetitionAsync();
        var judge = await _seed.JudgeAsync(competition, $"clean-{Guid.NewGuid():N}@brew.example");
        var entry = await _seed.EntryAsync(competition, $"stranger-{Guid.NewGuid():N}@brew.example");
        using var scope = factory.Services.CreateScope();

        var conflicts = await Checker(scope).FindConflictsAsync(competition, [judge], [entry], TestContext.Current.CancellationToken);

        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task FindConflictsIsScopedToTheGivenCompetition()
    {
        var competition = await _seed.CompetitionAsync();
        var otherCompetition = await _seed.CompetitionAsync();
        var email = $"shared-{Guid.NewGuid():N}@brew.example";
        var judge = await _seed.JudgeAsync(competition, email);
        var foreignEntry = await _seed.EntryAsync(otherCompetition, email);
        using var scope = factory.Services.CreateScope();

        var conflicts = await Checker(scope).FindConflictsAsync(competition, [judge], [foreignEntry], TestContext.Current.CancellationToken);

        Assert.Empty(conflicts);
    }

    [Fact]
    public async Task FindConflictsWithEmptyInputsReturnsEmpty()
    {
        var competition = await _seed.CompetitionAsync();
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Checker(scope).FindConflictsAsync(competition, [], [], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetEntriesOwnedByJudgesMapsEachJudgeToItsEntriesAndEmptyForNone()
    {
        var competition = await _seed.CompetitionAsync();
        var otherCompetition = await _seed.CompetitionAsync();
        var ownerEmail = $"owner-{Guid.NewGuid():N}@brew.example";
        var owner = await _seed.JudgeAsync(competition, ownerEmail);
        var bystander = await _seed.JudgeAsync(competition, $"by-{Guid.NewGuid():N}@brew.example");
        var asParticipant = await _seed.EntryAsync(competition, ownerEmail.ToUpperInvariant());
        var asCollaborator = await _seed.EntryAsync(competition, $"x-{Guid.NewGuid():N}@brew.example", [ownerEmail]);
        await _seed.EntryAsync(otherCompetition, ownerEmail);
        using var scope = factory.Services.CreateScope();

        var owned = await Checker(scope).GetEntriesOwnedByJudgesAsync(competition, [owner, bystander], TestContext.Current.CancellationToken);

        Assert.Equal(new HashSet<Guid> { asParticipant, asCollaborator }, owned[owner].ToHashSet());
        Assert.Empty(owned[bystander]);
    }

    [Fact]
    public async Task GetEntriesOwnedByJudgesWithNoJudgesReturnsEmpty()
    {
        var competition = await _seed.CompetitionAsync();
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Checker(scope).GetEntriesOwnedByJudgesAsync(competition, [], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetCoOwnerJudgesMapsEachEntryToTheJudgesThatOwnItAndEmptyForNone()
    {
        var competition = await _seed.CompetitionAsync();
        var otherCompetition = await _seed.CompetitionAsync();
        var participantEmail = $"p-{Guid.NewGuid():N}@brew.example";
        var collaboratorEmail = $"c-{Guid.NewGuid():N}@brew.example";
        var participantJudge = await _seed.JudgeAsync(competition, participantEmail);
        var collaboratorJudge = await _seed.JudgeAsync(competition, collaboratorEmail.ToUpperInvariant());
        await _seed.JudgeAsync(otherCompetition, participantEmail);
        var shared = await _seed.EntryAsync(competition, participantEmail, [collaboratorEmail]);
        var unowned = await _seed.EntryAsync(competition, $"nobody-{Guid.NewGuid():N}@brew.example");
        using var scope = factory.Services.CreateScope();

        var coOwners = await Checker(scope).GetCoOwnerJudgesAsync(competition, [shared, unowned], TestContext.Current.CancellationToken);

        Assert.Equal(new HashSet<Guid> { participantJudge, collaboratorJudge }, coOwners[shared].ToHashSet());
        Assert.Empty(coOwners[unowned]);
    }

    [Fact]
    public async Task GetCoOwnerJudgesWithNoEntriesReturnsEmpty()
    {
        var competition = await _seed.CompetitionAsync();
        using var scope = factory.Services.CreateScope();

        Assert.Empty(await Checker(scope).GetCoOwnerJudgesAsync(competition, [], TestContext.Current.CancellationToken));
    }
}
