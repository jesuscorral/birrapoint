using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging.Tests;

/// <summary>The handler's effect and its inbox row commit together, so a redelivery changes nothing (FR-079,
/// FR-084, SC-019). Tests drive the transport directly to redeliver on demand.</summary>
public sealed class InboxRunnerTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static IntegrationEventEnvelope Envelope(Guid thingId, Guid? id = null) =>
        EventSerializer.ToEnvelope(EventDescriptor.For<ThingHappened>(), new ThingHappened(thingId), id ?? Guid.CreateVersion7(), DateTimeOffset.UtcNow);

    private static Task<DeliveryOutcome> SendAsync(MessagingHost host, IntegrationEventEnvelope envelope) =>
        host.Services.GetRequiredService<IIntegrationEventTransport>().SendAsync(envelope, TestContext.Current.CancellationToken);

    [Fact]
    public async Task TheEffectAndTheInboxRowCommitTogetherAndRedeliveryChangesNothing()
    {
        await fixture.ResetAsync();
        var probe = new Probe();
        await using var host = await fixture.StartAsync(probe: probe, dispatcherEnabled: false);
        var thingId = Guid.NewGuid();
        var envelope = Envelope(thingId);

        for (var i = 0; i < 3; i++)
        {
            await SendAsync(host, envelope);
        }

        Assert.Equal(
            [Handlers.ThingName("FirstHandler", thingId), Handlers.ThingName("SecondHandler", thingId)],
            await fixture.ThingNamesAsync());
        var shipping = Assert.Single(await fixture.InboxRowsAsync("Shipping"));
        Assert.Equal(("FirstHandler", envelope.Id), (shipping.Handler, shipping.EventId));
        var orders = Assert.Single(await fixture.InboxRowsAsync("Orders"));
        Assert.Equal(("SecondHandler", envelope.Id), (orders.Handler, orders.EventId));
        Assert.Equal(1, probe.CallsOf("FirstHandler"));
        Assert.Equal(1, probe.CallsOf("SecondHandler"));
    }

    [Fact]
    public async Task AHandlerThatThrowsRollsBackItsInboxRowAndItsEffect()
    {
        await fixture.ResetAsync();
        var probe = new Probe { OnHandle = (_, _, _) => throw new InvalidOperationException("boom") };
        await using var host = await fixture.StartAsync(m => m.Subscribe<ThingHappened, FirstHandler>("Shipping"), probe: probe, dispatcherEnabled: false);

        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(host, Envelope(Guid.NewGuid())));

        Assert.Empty(await fixture.InboxRowsAsync("Shipping"));
        Assert.Empty(await fixture.ThingNamesAsync());
    }

    [Fact]
    public async Task WhenOneOfTwoSubscribersFailsTheRetryRunsOnlyTheOneThatFailed()
    {
        await fixture.ResetAsync();
        var failFirst = true;
        var probe = new Probe
        {
            OnHandle = (handler, _, _) =>
                failFirst && handler == "FirstHandler" ? throw new InvalidOperationException("transient") : Task.CompletedTask,
        };
        await using var host = await fixture.StartAsync(probe: probe, dispatcherEnabled: false);
        var thingId = Guid.NewGuid();
        var envelope = Envelope(thingId);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(host, envelope));
        Assert.Contains("FirstHandler", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SecondHandler", error.Message, StringComparison.Ordinal);
        failFirst = false;
        await SendAsync(host, envelope);

        Assert.Equal(2, probe.CallsOf("FirstHandler"));
        Assert.Equal(1, probe.CallsOf("SecondHandler"));
        Assert.Equal(
            [Handlers.ThingName("FirstHandler", thingId), Handlers.ThingName("SecondHandler", thingId)],
            await fixture.ThingNamesAsync());
    }

    [Fact]
    public async Task OnCommittedCallbacksRunAfterTheCommitAndNeverAfterARollback()
    {
        await fixture.ResetAsync();
        var seenInboxRows = -1;
        var probe = new Probe
        {
            OnCommitted = async (_, _) => seenInboxRows = (await fixture.InboxRowsAsync("Shipping")).Count,
        };
        await using var host = await fixture.StartAsync(m => m.Subscribe<ThingHappened, FirstHandler>("Shipping"), probe: probe, dispatcherEnabled: false);

        await SendAsync(host, Envelope(Guid.NewGuid()));

        Assert.Single(probe.Committed);
        Assert.Equal(1, seenInboxRows);

        probe.OnHandle = (_, _, _) => throw new InvalidOperationException("boom");
        await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(host, Envelope(Guid.NewGuid())));

        Assert.Single(probe.Committed);
    }

    [Fact]
    public async Task AFailingCallbackNeverUndoesTheCommittedEffect()
    {
        await fixture.ResetAsync();
        var probe = new Probe { OnCommitted = (_, _) => throw new InvalidOperationException("emit failed") };
        await using var host = await fixture.StartAsync(m => m.Subscribe<ThingHappened, FirstHandler>("Shipping"), probe: probe, dispatcherEnabled: false);
        var thingId = Guid.NewGuid();

        await SendAsync(host, Envelope(thingId));

        Assert.Single(await fixture.InboxRowsAsync("Shipping"));
        Assert.Single(await fixture.ThingNamesAsync());
    }
}
