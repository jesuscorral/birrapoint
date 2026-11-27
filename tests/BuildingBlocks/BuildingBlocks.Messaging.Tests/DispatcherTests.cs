using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging.Tests;

/// <summary>Committed events reach every subscriber at least once, claimed atomically and in order (FR-078).</summary>
public sealed class DispatcherTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    /// <summary>Counts how many times each event is sent; the delay widens the race window between dispatchers.</summary>
    private sealed class CountingTransport(TimeSpan delay) : IIntegrationEventTransport
    {
        public ConcurrentQueue<Guid> Order { get; } = new();

        public ConcurrentDictionary<Guid, int> Sends { get; } = new();

        public async Task<DeliveryOutcome> SendAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
        {
            Order.Enqueue(envelope.Id);
            Sends.AddOrUpdate(envelope.Id, 1, (_, n) => n + 1);
            await Task.Delay(delay, cancellationToken);
            return DeliveryOutcome.Delivered;
        }
    }

    [Fact]
    public async Task ACommittedEventReachesEverySubscriberAndTheRowBecomesDelivered()
    {
        await fixture.ResetAsync();
        var probe = new Probe();
        await using var host = await fixture.StartAsync(probe: probe);
        var thingId = Guid.NewGuid();

        var id = await host.PublishAsync(new ThingHappened(thingId));

        await Wait.UntilDeliveredAsync(fixture, "Orders", id);
        Assert.Equal(
            [Handlers.ThingName("FirstHandler", thingId), Handlers.ThingName("SecondHandler", thingId)],
            await fixture.ThingNamesAsync());
        var row = await fixture.OutboxAsync("Orders", id);
        Assert.Null(row.LeaseOwner);
        Assert.Null(row.LeaseExpiresAt);
        Assert.Equal(0, row.Attempts);
    }

    [Fact]
    public async Task TwoDispatchersNeverDeliverTheSameRowConcurrently()
    {
        await fixture.ResetAsync();
        var transport = new CountingTransport(TimeSpan.FromMilliseconds(150));
        await using var first = await fixture.StartAsync(m => m.Publishes<ThingHappened>("Orders"), s => s.AddSingleton<IIntegrationEventTransport>(transport));
        await using var second = await fixture.StartAsync(m => m.Publishes<ThingHappened>("Orders"), s => s.AddSingleton<IIntegrationEventTransport>(transport));

        var ids = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            ids.Add(await first.PublishAsync(new ThingHappened(Guid.NewGuid())));
        }

        foreach (var id in ids)
        {
            await Wait.UntilDeliveredAsync(fixture, "Orders", id);
        }

        Assert.All(ids, id => Assert.Equal(1, transport.Sends[id]));
    }

    [Fact]
    public async Task ADeliveringRowWithAnExpiredLeaseIsClaimedAgainAfterACrash()
    {
        await fixture.ResetAsync();
        var probe = new Probe();
        var thingId = Guid.NewGuid();
        var crashed = await fixture.SeedOutboxAsync(
            "Orders",
            payloadJson: "{\"thingId\":\"" + thingId + "\"}",
            status: OutboxStatus.Delivering,
            leaseOwner: "crashed-process",
            leaseExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        await using var host = await fixture.StartAsync(probe: probe);

        await Wait.UntilDeliveredAsync(fixture, "Orders", crashed.Id);
        Assert.Equal(1, probe.CallsOf("FirstHandler"));
        var row = await fixture.OutboxAsync("Orders", crashed.Id);
        Assert.Equal(1, row.Attempts);
        Assert.Null(row.LeaseOwner);
    }

    [Fact]
    public async Task ALiveLeaseIsLeftAlone()
    {
        await fixture.ResetAsync();
        var transport = new CountingTransport(TimeSpan.Zero);
        var held = await fixture.SeedOutboxAsync(
            "Orders",
            status: OutboxStatus.Delivering,
            leaseOwner: "other-replica",
            leaseExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5));

        await using var host = await fixture.StartAsync(
            m => m.Publishes<ThingHappened>("Orders"), s => s.AddSingleton<IIntegrationEventTransport>(transport));
        var probeEvent = await host.PublishAsync(new ThingHappened(Guid.NewGuid()));
        await Wait.UntilDeliveredAsync(fixture, "Orders", probeEvent);

        Assert.False(transport.Sends.ContainsKey(held.Id));
        Assert.Equal(OutboxStatus.Delivering, (await fixture.OutboxAsync("Orders", held.Id)).Status);
    }

    [Fact]
    public async Task DueRowsAreClaimedInOccurredAtOrder()
    {
        await fixture.ResetAsync();
        var transport = new CountingTransport(TimeSpan.Zero);
        var now = DateTimeOffset.UtcNow;
        var third = await fixture.SeedOutboxAsync("Orders", occurredAt: now.AddSeconds(-1));
        var first = await fixture.SeedOutboxAsync("Orders", occurredAt: now.AddSeconds(-3));
        var second = await fixture.SeedOutboxAsync("Orders", occurredAt: now.AddSeconds(-2));

        await using var host = await fixture.StartAsync(
            m => m.Publishes<ThingHappened>("Orders"), s => s.AddSingleton<IIntegrationEventTransport>(transport));

        await Wait.UntilDeliveredAsync(fixture, "Orders", third.Id);
        Assert.Equal([first.Id, second.Id, third.Id], transport.Order.ToArray());
    }

    [Fact]
    public async Task ARetryNotYetDueIsNotClaimed()
    {
        await fixture.ResetAsync();
        var transport = new CountingTransport(TimeSpan.Zero);
        var later = await fixture.SeedOutboxAsync("Orders", attempts: 1, nextAttemptAt: DateTimeOffset.UtcNow.AddMinutes(10));

        await using var host = await fixture.StartAsync(
            m => m.Publishes<ThingHappened>("Orders"), s => s.AddSingleton<IIntegrationEventTransport>(transport));
        var probeEvent = await host.PublishAsync(new ThingHappened(Guid.NewGuid()));
        await Wait.UntilDeliveredAsync(fixture, "Orders", probeEvent);

        Assert.False(transport.Sends.ContainsKey(later.Id));
    }
}
