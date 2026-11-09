using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging.Tests;

/// <summary>The transport is a seam: another one can replace the in-process transport and neither publishers nor
/// handlers change (FR-086).</summary>
public sealed class TransportAbstractionTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private sealed class CapturingTransport : IIntegrationEventTransport
    {
        public System.Collections.Concurrent.ConcurrentQueue<IntegrationEventEnvelope> Sent { get; } = new();

        public Task<DeliveryOutcome> SendAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
        {
            Sent.Enqueue(envelope);
            return Task.FromResult(DeliveryOutcome.Delivered);
        }
    }

    [Fact]
    public async Task ARegisteredTransportReceivesTheEnvelopesInsteadOfTheLocalOne()
    {
        await fixture.ResetAsync();
        var transport = new CapturingTransport();
        await using var host = await fixture.StartAsync(
            configure: m => m.Publishes<ThingHappened>("Orders"),
            services: s => s.AddSingleton<IIntegrationEventTransport>(transport));

        var thingId = Guid.NewGuid();
        var eventId = await host.PublishAsync(new ThingHappened(thingId));

        await Wait.UntilAsync(() => Task.FromResult(!transport.Sent.IsEmpty), "transport received the envelope");
        var envelope = Assert.Single(transport.Sent);
        Assert.Equal(eventId, envelope.Id);
        Assert.Equal("test.thing-happened", envelope.Type);
        Assert.Equal(1, envelope.Version);
        Assert.Contains(thingId.ToString(), envelope.PayloadJson, StringComparison.Ordinal);
        await Wait.UntilAsync(
            async () => (await fixture.OutboxAsync("Orders", eventId)).Status == OutboxStatus.Delivered,
            "row delivered once the transport accepted it");
    }
}
