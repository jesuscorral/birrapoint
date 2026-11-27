using System.Net;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;

namespace BirraPoint.Api.IntegrationTests.IntegrationEvents;

/// <summary>
/// Finalizing travels as the <c>organization.competition-finalized</c> integration event instead of a
/// same-transaction MediatR notification (FR-077, FR-079, SC-019, US18 scenarios 1-3): the event is stored with the
/// state change, nothing happens in Publication until it is delivered, and a redelivery changes nothing.
/// </summary>
public sealed class CompetitionFinalizedFlowTests(ApiFactory factory) : IntegrationEventsTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task FinalizeStoresOneEventWithTheStateChangeAndPublicationReactsOnlyAfterDeliveryExactlyOnce()
    {
        Guid competitionId;
        Guid eventId;

        // Delivery held: the application "stops" between commit and delivery.
        await using (var held = NewHost(dispatcherEnabled: false))
        {
            using var organizer = OrganizerClient(held, NewOrganizerSub());
            var closed = await SeedClosedCompetitionAsync(held, organizer, "Finalized");
            competitionId = closed.CompetitionId;

            var response = await TransitionAsync(organizer, competitionId, "Finalized");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var rows = await MessagingDb.OutboxAsync(ConnectionString, MessagingDb.Organization, MessagingDb.CompetitionFinalized, competitionId);
            var row = Assert.Single(rows);
            Assert.Equal(1, row.Version);
            Assert.Equal("Pending", row.Status);
            eventId = row.Id;
            Assert.Equal(0, await MessagingDb.JobCountAsync(ConnectionString, competitionId, DispatchJobType.GeneratePdfs));
            Assert.Equal(0, await MessagingDb.InboxCountAsync(ConnectionString, MessagingDb.Publication, eventId));
        }

        // The application restarts with the dispatcher on.
        await using var running = NewHost(dispatcherEnabled: true);
        _ = running.Services; // starts the host and its dispatcher

        await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Organization, eventId);
        await EventualAssert.EqualAsync(
            1L, () => MessagingDb.JobCountAsync(ConnectionString, competitionId, DispatchJobType.GeneratePdfs),
            "one GeneratePdfs job staged by Publication");
        Assert.Equal(1, await MessagingDb.InboxCountAsync(ConnectionString, MessagingDb.Publication, eventId));

        // Delivered three more times: no extra job, no extra inbox row (SC-019).
        for (var redelivery = 0; redelivery < 3; redelivery++)
        {
            await MessagingDb.ForceRedeliveryAsync(ConnectionString, MessagingDb.Organization, eventId);
            await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Organization, eventId);
        }

        Assert.Equal(1, await MessagingDb.JobCountAsync(ConnectionString, competitionId, DispatchJobType.GeneratePdfs));
        Assert.Equal(1, await MessagingDb.InboxCountAsync(ConnectionString, MessagingDb.Publication, eventId));
    }

    [Fact]
    public async Task ConcurrentFinalizeRequestsStoreExactlyOneEventAndOneJob()
    {
        await using var host = NewHost();
        using var organizer = OrganizerClient(host, NewOrganizerSub());
        var closed = await SeedClosedCompetitionAsync(host, organizer, "FinalizeRace");

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => TransitionAsync(organizer, closed.CompetitionId, "Finalized")));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var row = Assert.Single(await MessagingDb.OutboxAsync(
            ConnectionString, MessagingDb.Organization, MessagingDb.CompetitionFinalized, closed.CompetitionId));
        await MessagingDb.AwaitDeliveredAsync(ConnectionString, MessagingDb.Organization, row.Id);
        await EventualAssert.EqualAsync(
            1L, () => MessagingDb.JobCountAsync(ConnectionString, closed.CompetitionId, DispatchJobType.GeneratePdfs),
            "one GeneratePdfs job");
    }

    [Fact]
    public async Task FinalizeNeverStoresAnEventForAnyOtherTransition()
    {
        await using var host = NewHost(dispatcherEnabled: false);
        using var organizer = OrganizerClient(host, NewOrganizerSub());
        var competitionId = await CreateCompetitionAsync(organizer);

        Assert.Equal(HttpStatusCode.OK, (await TransitionAsync(organizer, competitionId, "Active")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TransitionAsync(organizer, competitionId, "InEvaluation")).StatusCode);

        Assert.Empty(await MessagingDb.OutboxAsync(
            ConnectionString, MessagingDb.Organization, MessagingDb.CompetitionFinalized, competitionId));
    }
}
