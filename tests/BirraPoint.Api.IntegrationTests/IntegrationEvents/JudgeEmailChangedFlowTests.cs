using System.Net;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BirraPoint.Api.IntegrationTests.IntegrationEvents;

/// <summary>
/// A judge's email correction re-applies the conflict rules against the new address (FR-017/FR-018 debt, FR-082,
/// US20): flags are recomputed in the response transaction, <c>organization.judge-email-changed</c> is stored with
/// them, and Judging alerts the organizers with <c>JudgeConflictDetected</c> after its own commit (FR-084) when the
/// judge sits at a table holding an entry they now own. The judge stays seated.
/// </summary>
public sealed class JudgeEmailChangedFlowTests(ApiFactory factory) : IntegrationEventsTestBase(factory), IClassFixture<ApiFactory>
{
    private static readonly TimeSpan AlertTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Absence is proven after the reaction committed; this is the grace for a push that would follow it.</summary>
    private static readonly TimeSpan NoAlertWindow = TimeSpan.FromSeconds(1.5);

    private sealed record Scenario(
        string OrganizerSub, Guid CompetitionId, Guid JudgeId, string JudgeDisplayName, string OldEmail,
        Guid TableId, Guid TableEntryId, string TableEntryOwnerEmail);

    /// <summary>Draft competition; judge J (never logged in) seated at a table whose only sample belongs to
    /// another brewer. J's old address owns nothing yet.</summary>
    private static async Task<Scenario> SeedScenarioAsync(WebApplicationFactory<Program> host, HttpClient organizer, string organizerSub)
    {
        var services = host.Services;
        var competitionId = await CreateCompetitionAsync(organizer);
        var oldEmail = NewEmail("judge-old");
        var judgeId = await SeedJudgeAsync(services, competitionId, oldEmail);
        var tableEntryOwnerEmail = NewEmail("table-owner");
        var participantId = await SeedParticipantAsync(services, competitionId, "Secret Table Brewer", tableEntryOwnerEmail);
        var tableEntryId = await SeedEntryAsync(services, competitionId, participantId, "Secret Table Brew");
        var tableId = await SeedTableAsync(services, competitionId, "Table A");
        await SeedTableSeatAsync(services, tableId, judgeId);
        await SeedTableSampleAsync(services, tableId, tableEntryId);
        return new Scenario(
            organizerSub, competitionId, judgeId, oldEmail.Split('@')[0], oldEmail, tableId, tableEntryId, tableEntryOwnerEmail);
    }

    private static async Task<HubTestClient> OrganizerHubAsync(WebApplicationFactory<Program> host, Scenario s)
    {
        var hub = await HubTestClient.ConnectAsync(host.Server, TestJwtIssuer.IssueToken(s.OrganizerSub, null, "ORGANIZER"));
        await hub.InvokeAsync("JoinCompetitionAsOrganizer", s.CompetitionId);
        return hub;
    }

    private async Task<OutboxRow> SingleEmailEventAsync(Scenario s)
    {
        var row = Assert.Single(await MessagingDb.OutboxAsync(
            ConnectionString, MessagingDb.Organization, MessagingDb.JudgeEmailChanged, s.CompetitionId));
        Assert.Equal(1, row.Version);
        Assert.Equal(s.JudgeId, row.Property("judgeId").GetGuid());
        return row;
    }

    private Task AwaitProcessedByJudgingAsync(Guid eventId) =>
        EventualAssert.TrueAsync(
            async () => await MessagingDb.InboxCountAsync(ConnectionString, MessagingDb.Judging, eventId) == 1,
            "judge-email-changed processed by Judging");

    [Fact]
    public async Task ACorrectedEmailThatNowOwnsAnEntryAtTheJudgesTableFlagsItAtOnceAndAlertsOrganizersAfterDelivery()
    {
        await using var host = NewHost();
        var organizerSub = NewOrganizerSub();
        using var organizer = OrganizerClient(host, organizerSub);
        var s = await SeedScenarioAsync(host, organizer, organizerSub);
        await using var hub = await OrganizerHubAsync(host, s);

        var response = await UpdateJudgeEmailAsync(organizer, s.CompetitionId, s.JudgeId, s.TableEntryOwnerEmail);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Recomputed by Organization in the response transaction: no waiting.
        Assert.True(await IsNotValidForBosAsync(host.Services, s.TableEntryId));
        var evt = await SingleEmailEventAsync(s);

        var alert = await hub.NextAsync("JudgeConflictDetected", AlertTimeout);
        Assert.NotNull(alert);
        Assert.Equal(s.TableId, alert.Value.GetProperty("tableId").GetGuid());
        Assert.Equal(s.JudgeId, alert.Value.GetProperty("judgeId").GetGuid());
        Assert.Equal(s.JudgeDisplayName, alert.Value.GetProperty("judgeDisplayName").GetString());
        Assert.Equal([s.TableEntryId], alert.Value.GetProperty("beerEntryIds").EnumerateArray().Select(e => e.GetGuid()));

        Assert.True(await IsSeatedAsync(host.Services, s.TableId, s.JudgeId));
        await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Organization, evt.Id);
        Assert.Equal(1, await MessagingDb.InboxCountAsync(ConnectionString, MessagingDb.Judging, evt.Id));
    }

    [Fact]
    public async Task RedeliveringTheEmailEventSendsNoSecondAlert()
    {
        await using var host = NewHost();
        var organizerSub = NewOrganizerSub();
        using var organizer = OrganizerClient(host, organizerSub);
        var s = await SeedScenarioAsync(host, organizer, organizerSub);
        await using var hub = await OrganizerHubAsync(host, s);
        await UpdateJudgeEmailAsync(organizer, s.CompetitionId, s.JudgeId, s.TableEntryOwnerEmail);
        var evt = await SingleEmailEventAsync(s);
        Assert.NotNull(await hub.NextAsync("JudgeConflictDetected", AlertTimeout));
        await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Organization, evt.Id);

        for (var redelivery = 0; redelivery < 2; redelivery++)
        {
            await MessagingDb.ForceRedeliveryAsync(ConnectionString, MessagingDb.Organization, evt.Id);
            await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Organization, evt.Id);
        }

        Assert.Null(await hub.NextAsync("JudgeConflictDetected", NoAlertWindow));
        Assert.Equal(1, await MessagingDb.InboxCountAsync(ConnectionString, MessagingDb.Judging, evt.Id));
    }

    [Fact]
    public async Task ACorrectedEmailOwningAnEntryElsewhereFlagsItButSendsNoAlert()
    {
        await using var host = NewHost();
        var organizerSub = NewOrganizerSub();
        using var organizer = OrganizerClient(host, organizerSub);
        var s = await SeedScenarioAsync(host, organizer, organizerSub);
        var newEmail = NewEmail("judge-new");
        var participantId = await SeedParticipantAsync(host.Services, s.CompetitionId, "Elsewhere Brewer", newEmail);
        var elsewhereEntryId = await SeedEntryAsync(host.Services, s.CompetitionId, participantId, "Elsewhere Brew");
        await using var hub = await OrganizerHubAsync(host, s);

        var response = await UpdateJudgeEmailAsync(organizer, s.CompetitionId, s.JudgeId, newEmail);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await IsNotValidForBosAsync(host.Services, elsewhereEntryId));
        var evt = await SingleEmailEventAsync(s);
        await AwaitProcessedByJudgingAsync(evt.Id);
        Assert.Null(await hub.NextAsync("JudgeConflictDetected", NoAlertWindow));
        Assert.True(await IsSeatedAsync(host.Services, s.TableId, s.JudgeId));
    }

    [Fact]
    public async Task TheOldAddressFlagClearsInTheSameResponseWhenNoCoOwnerIsEngaged()
    {
        await using var host = NewHost();
        var organizerSub = NewOrganizerSub();
        using var organizer = OrganizerClient(host, organizerSub);
        var s = await SeedScenarioAsync(host, organizer, organizerSub);
        var participantId = await SeedParticipantAsync(host.Services, s.CompetitionId, "Old Address Brewer", s.OldEmail);
        var oldOwnedEntryId = await SeedEntryAsync(host.Services, s.CompetitionId, participantId, "Old Address Brew");
        await MessagingDb.ExecuteAsync(
            ConnectionString, "UPDATE organization.\"BeerEntries\" SET \"NotValidForBos\" = true WHERE \"Id\" = @id", ("id", oldOwnedEntryId));
        await using var hub = await OrganizerHubAsync(host, s);

        var response = await UpdateJudgeEmailAsync(organizer, s.CompetitionId, s.JudgeId, NewEmail("judge-new"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await IsNotValidForBosAsync(host.Services, oldOwnedEntryId));
        var evt = await SingleEmailEventAsync(s);
        await AwaitProcessedByJudgingAsync(evt.Id);
        Assert.Null(await hub.NextAsync("JudgeConflictDetected", NoAlertWindow));
    }
}
