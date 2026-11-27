using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BirraPoint.Api.IntegrationTests.IntegrationEvents;

/// <summary>
/// Table edits travel as <c>judging.table-judges-changed</c> and Organization recomputes the Best of Show flags
/// from current engagement when it receives them (FR-018, FR-079, US18 scenario 5): same result whatever the
/// delivery order, and a redelivery has no effect.
/// </summary>
public sealed class TableJudgesChangedFlowTests(ApiFactory factory) : IntegrationEventsTestBase(factory), IClassFixture<ApiFactory>
{
    private sealed record Scenario(
        Guid CompetitionId, Guid TableId, string TableName, Guid OwnerJudgeId, Guid BystanderJudgeId,
        Guid OwnedEntryId, Guid SampleEntryId, string OwnerEmail);

    /// <summary>A Draft competition, a table with a bystander judge and one unrelated sample, and a judge
    /// (not yet seated) who owns another entry.</summary>
    private static async Task<Scenario> SeedScenarioAsync(WebApplicationFactory<Program> host, HttpClient organizer)
    {
        var services = host.Services;
        var competitionId = await CreateCompetitionAsync(organizer);
        var ownerEmail = NewEmail("judge-owner");
        var ownerParticipantId = await SeedParticipantAsync(services, competitionId, "Judge Brewer", ownerEmail);
        var ownedEntryId = await SeedEntryAsync(services, competitionId, ownerParticipantId, "Judge's Own Brew");
        var otherParticipantId = await SeedParticipantAsync(services, competitionId, "Other Brewer", NewEmail("other"));
        var sampleEntryId = await SeedEntryAsync(services, competitionId, otherParticipantId, "Unrelated Brew");
        var ownerJudgeId = await SeedJudgeAsync(services, competitionId, ownerEmail);
        var bystanderJudgeId = await SeedJudgeAsync(services, competitionId, NewEmail("bystander"));

        var tableName = $"Table {Guid.NewGuid():N}";
        var tableId = await ReadIdAsync(await CreateTableAsync(organizer, competitionId, tableName, [bystanderJudgeId], [sampleEntryId]));
        return new Scenario(competitionId, tableId, tableName, ownerJudgeId, bystanderJudgeId, ownedEntryId, sampleEntryId, ownerEmail);
    }

    private static Task AwaitFlagAsync(IServiceProvider services, Guid entryId, bool expected) =>
        EventualAssert.EqualAsync(
            expected, () => IsNotValidForBosAsync(services, entryId), $"NotValidForBos of {entryId} becomes {expected}");

    private async Task<OutboxRow> LastTableEventAsync(Guid competitionId) =>
        (await MessagingDb.OutboxAsync(ConnectionString, MessagingDb.Judging, MessagingDb.TableJudgesChanged, competitionId))[^1];

    [Fact]
    public async Task AddingAJudgeWhoOwnsAnEntryFlagsItOnceTheEventIsDelivered()
    {
        await using var host = NewHost();
        using var organizer = OrganizerClient(host, NewOrganizerSub());
        var s = await SeedScenarioAsync(host, organizer);

        var response = await UpdateTableAsync(organizer, s.CompetitionId, s.TableId, s.TableName, [s.BystanderJudgeId, s.OwnerJudgeId], [s.SampleEntryId]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // The response still reports the flagged entries at once; only the persisted flag is eventual.
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains(s.OwnedEntryId, body.RootElement.GetProperty("bosFlaggedEntryIds").EnumerateArray().Select(e => e.GetGuid()));

        var evt = await LastTableEventAsync(s.CompetitionId);
        Assert.Equal(1, evt.Version);
        Assert.Equal(s.TableId, evt.Property("tableId").GetGuid());
        Assert.Equal([s.OwnerJudgeId], evt.Property("addedJudgeIds").EnumerateArray().Select(e => e.GetGuid()));
        Assert.Empty(evt.Property("removedJudgeIds").EnumerateArray());

        await AwaitFlagAsync(host.Services, s.OwnedEntryId, expected: true);
        await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Judging, evt.Id);
        Assert.Equal(1, await MessagingDb.InboxCountAsync(ConnectionString, MessagingDb.Organization, evt.Id));
        Assert.False(await IsNotValidForBosAsync(host.Services, s.SampleEntryId));
    }

    [Fact]
    public async Task RemovingTheJudgeClearsTheFlagWhenNoCoOwnerIsEngaged()
    {
        await using var host = NewHost();
        using var organizer = OrganizerClient(host, NewOrganizerSub());
        var s = await SeedScenarioAsync(host, organizer);
        await UpdateTableAsync(organizer, s.CompetitionId, s.TableId, s.TableName, [s.BystanderJudgeId, s.OwnerJudgeId], [s.SampleEntryId]);
        await AwaitFlagAsync(host.Services, s.OwnedEntryId, expected: true);

        var response = await UpdateTableAsync(organizer, s.CompetitionId, s.TableId, s.TableName, [s.BystanderJudgeId], [s.SampleEntryId]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var evt = await LastTableEventAsync(s.CompetitionId);
        Assert.Equal([s.OwnerJudgeId], evt.Property("removedJudgeIds").EnumerateArray().Select(e => e.GetGuid()));
        await AwaitFlagAsync(host.Services, s.OwnedEntryId, expected: false);
    }

    [Fact]
    public async Task RemovingTheJudgeKeepsTheFlagWhileACoOwnerStaysEngaged()
    {
        await using var host = NewHost();
        using var organizer = OrganizerClient(host, NewOrganizerSub());
        var s = await SeedScenarioAsync(host, organizer);
        var services = host.Services;

        // A collaborator of the same entry, seated at a second table.
        var collaboratorEmail = NewEmail("collaborator");
        var collaboratorJudgeId = await SeedJudgeAsync(services, s.CompetitionId, collaboratorEmail);
        var sharedParticipantId = await SeedParticipantAsync(services, s.CompetitionId, "Shared Brewer", NewEmail("shared"));
        var sharedEntryId = await SeedEntryAsync(
            services, s.CompetitionId, sharedParticipantId, "Shared Brew", collaboratorEmails: [s.OwnerEmail, collaboratorEmail]);
        var table2EntryId = await SeedEntryAsync(services, s.CompetitionId, sharedParticipantId, "Table 2 Brew");
        await CreateTableAsync(organizer, s.CompetitionId, $"Table2 {Guid.NewGuid():N}", [collaboratorJudgeId], [table2EntryId]);
        await AwaitFlagAsync(services, sharedEntryId, expected: true);

        await UpdateTableAsync(organizer, s.CompetitionId, s.TableId, s.TableName, [s.BystanderJudgeId, s.OwnerJudgeId], [s.SampleEntryId]);
        await EventualAssert.OutboxDrainedAsync(ConnectionString);
        var removal = await UpdateTableAsync(organizer, s.CompetitionId, s.TableId, s.TableName, [s.BystanderJudgeId], [s.SampleEntryId]);
        Assert.Equal(HttpStatusCode.OK, removal.StatusCode);
        var evt = await LastTableEventAsync(s.CompetitionId);
        await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Judging, evt.Id);
        await EventualAssert.TrueAsync(
            async () => await MessagingDb.InboxCountAsync(ConnectionString, MessagingDb.Organization, evt.Id) == 1,
            "removal processed by Organization");

        Assert.True(await IsNotValidForBosAsync(services, sharedEntryId));
        Assert.False(await IsNotValidForBosAsync(services, s.SampleEntryId));
    }

    [Fact]
    public async Task TwoTableEditsDeliveredInReverseOrderConvergeToTheSameFlags()
    {
        Guid ownedEntryId;
        Guid removalEventId;
        Guid additionEventId;

        // Held: the removal (older) and the re-addition (newer) of the same judge both wait in the outbox.
        await using (var held = NewHost(dispatcherEnabled: false))
        {
            using var organizer = OrganizerClient(held, NewOrganizerSub());
            var services = held.Services;
            var competitionId = await CreateCompetitionAsync(organizer);
            var ownerEmail = NewEmail("judge-owner");
            var ownerParticipantId = await SeedParticipantAsync(services, competitionId, "Judge Brewer", ownerEmail);
            ownedEntryId = await SeedEntryAsync(services, competitionId, ownerParticipantId, "Judge's Own Brew");
            var otherParticipantId = await SeedParticipantAsync(services, competitionId, "Other Brewer", NewEmail("other"));
            var sampleEntryId = await SeedEntryAsync(services, competitionId, otherParticipantId, "Unrelated Brew");
            var ownerJudgeId = await SeedJudgeAsync(services, competitionId, ownerEmail);
            var bystanderJudgeId = await SeedJudgeAsync(services, competitionId, NewEmail("bystander"));

            // Seat the judge directly, as an earlier delivered edit would have left it: flag already set.
            var tableName = $"Table {Guid.NewGuid():N}";
            var tableId = await SeedTableAsync(services, competitionId, tableName);
            await SeedTableSeatAsync(services, tableId, bystanderJudgeId);
            await SeedTableSeatAsync(services, tableId, ownerJudgeId);
            await SeedTableSampleAsync(services, tableId, sampleEntryId);
            await MessagingDb.ExecuteAsync(
                ConnectionString, "UPDATE organization.\"BeerEntries\" SET \"NotValidForBos\" = true WHERE \"Id\" = @id", ("id", ownedEntryId));

            Assert.Equal(HttpStatusCode.OK, (await UpdateTableAsync(organizer, competitionId, tableId, tableName, [bystanderJudgeId], [sampleEntryId])).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await UpdateTableAsync(organizer, competitionId, tableId, tableName, [bystanderJudgeId, ownerJudgeId], [sampleEntryId])).StatusCode);

            var events = await MessagingDb.OutboxAsync(ConnectionString, MessagingDb.Judging, MessagingDb.TableJudgesChanged, competitionId);
            Assert.Equal(2, events.Count);
            Assert.All(events, e => Assert.Equal("Pending", e.Status));
            removalEventId = events[0].Id;
            additionEventId = events[1].Id;
            Assert.Equal([ownerJudgeId], events[0].Property("removedJudgeIds").EnumerateArray().Select(e => e.GetGuid()));
            Assert.Equal([ownerJudgeId], events[1].Property("addedJudgeIds").EnumerateArray().Select(e => e.GetGuid()));

            // Make the dispatcher claim the re-addition first (it claims in OccurredAt order).
            await MessagingDb.ExecuteAsync(
                ConnectionString,
                "UPDATE judging.\"OutboxMessages\" SET \"OccurredAt\" = CASE WHEN \"Id\" = @removal THEN now() ELSE now() - interval '1 minute' END " +
                "WHERE \"Id\" IN (@removal, @addition)",
                ("removal", removalEventId), ("addition", additionEventId));
        }

        await using var running = NewHost();
        _ = running.Services;
        await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Judging, additionEventId);
        await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Judging, removalEventId);
        await EventualAssert.TrueAsync(
            async () => await MessagingDb.InboxCountAsync(ConnectionString, MessagingDb.Organization, removalEventId) == 1
                && await MessagingDb.InboxCountAsync(ConnectionString, MessagingDb.Organization, additionEventId) == 1,
            "both events processed by Organization");

        // The judge is seated at the end, so the entry stays flagged whichever event came last.
        Assert.True(await IsNotValidForBosAsync(running.Services, ownedEntryId));
    }

    [Fact]
    public async Task RedeliveringATableEventThreeTimesChangesNothing()
    {
        await using var host = NewHost();
        using var organizer = OrganizerClient(host, NewOrganizerSub());
        var s = await SeedScenarioAsync(host, organizer);
        await UpdateTableAsync(organizer, s.CompetitionId, s.TableId, s.TableName, [s.BystanderJudgeId, s.OwnerJudgeId], [s.SampleEntryId]);
        var evt = await LastTableEventAsync(s.CompetitionId);
        await AwaitFlagAsync(host.Services, s.OwnedEntryId, expected: true);
        await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Judging, evt.Id);

        // Un-flag behind the module's back: only a handler that runs again could set it back.
        await MessagingDb.ExecuteAsync(
            ConnectionString, "UPDATE organization.\"BeerEntries\" SET \"NotValidForBos\" = false WHERE \"Id\" = @id", ("id", s.OwnedEntryId));

        for (var redelivery = 0; redelivery < 3; redelivery++)
        {
            await MessagingDb.ForceRedeliveryAsync(ConnectionString, MessagingDb.Judging, evt.Id);
            await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Judging, evt.Id);
        }

        Assert.False(await IsNotValidForBosAsync(host.Services, s.OwnedEntryId));
        Assert.Equal(1, await MessagingDb.InboxCountAsync(ConnectionString, MessagingDb.Organization, evt.Id));
    }
}
