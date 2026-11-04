using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Integration;

/// <summary>IResultRecipients (ADR-0033 phase 3): participants and their entries for results delivery
/// (FR-040/FR-041). Publication runs after tasting ends, so entrant contact data is allowed here.</summary>
public sealed class ResultRecipientsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly ContractSeeder _seed = new(factory.Services);

    private static IResultRecipients Recipients(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IResultRecipients>();

    private static string Email() => $"entrant-{Guid.NewGuid():N}@brew.example";

    [Fact]
    public async Task GetEntriesReturnsEveryEntryOfTheCompetitionWithItsParticipant()
    {
        var competition = await _seed.CompetitionAsync();
        var otherCompetition = await _seed.CompetitionAsync();
        var first = await _seed.EntryAsync(competition, Email(), configure: e => e.StyleCode = "10A");
        var second = await _seed.EntryAsync(competition, Email());
        await _seed.EntryAsync(otherCompetition, Email());
        using var scope = factory.Services.CreateScope();

        var entries = await Recipients(scope).GetEntriesAsync(competition, TestContext.Current.CancellationToken);

        Assert.Equal([first, second], entries.Select(e => e.EntryId).Order());
        var firstEntry = entries.Single(e => e.EntryId == first);
        Assert.Equal("10A", firstEntry.StyleCode);
        Assert.False(string.IsNullOrEmpty(firstEntry.BlindCode));
        Assert.NotEqual(Guid.Empty, firstEntry.ParticipantId);
    }

    [Fact]
    public async Task GetParticipantEntriesReturnsOnlyThatParticipantsEntries()
    {
        var competition = await _seed.CompetitionAsync();
        var entry = await _seed.EntryAsync(competition, Email());
        await _seed.EntryAsync(competition, Email());
        using var scope = factory.Services.CreateScope();
        var participant = (await Recipients(scope).GetEntriesAsync(competition, TestContext.Current.CancellationToken))
            .Single(e => e.EntryId == entry).ParticipantId;

        var entries = await Recipients(scope).GetParticipantEntriesAsync(participant, TestContext.Current.CancellationToken);

        Assert.Equal(entry, Assert.Single(entries).EntryId);
    }

    [Fact]
    public async Task GetRecipientsReturnsEveryParticipantOfTheCompetitionWithTheirEmail()
    {
        var competition = await _seed.CompetitionAsync();
        var otherCompetition = await _seed.CompetitionAsync();
        var email = Email();
        await _seed.EntryAsync(competition, email);
        await _seed.EntryAsync(otherCompetition, Email());
        using var scope = factory.Services.CreateScope();

        var recipients = await Recipients(scope).GetRecipientsAsync(competition, TestContext.Current.CancellationToken);

        Assert.Equal(email, Assert.Single(recipients).Email);
    }

    [Fact]
    public async Task GetRecipientReturnsTheParticipantOrNull()
    {
        var competition = await _seed.CompetitionAsync();
        var email = Email();
        await _seed.EntryAsync(competition, email);
        using var scope = factory.Services.CreateScope();
        var recipient = Assert.Single(await Recipients(scope).GetRecipientsAsync(competition, TestContext.Current.CancellationToken));

        var found = await Recipients(scope).GetRecipientAsync(recipient.ParticipantId, TestContext.Current.CancellationToken);

        Assert.Equal(recipient, found);
        Assert.Null(await Recipients(scope).GetRecipientAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }
}
