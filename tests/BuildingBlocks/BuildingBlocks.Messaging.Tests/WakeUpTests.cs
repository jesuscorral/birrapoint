using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging.Tests;

/// <summary>A commit that includes outbox rows wakes the dispatcher at once, so reactions stay well under a second
/// (FR-089); a rollback or a save without outbox rows signals nothing (R-106-4). Signals are counted with the
/// dispatcher off, and latency is measured with a 1 h safety-net poll so only the wake-up can deliver.</summary>
public sealed class WakeUpTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan LongPoll = TimeSpan.FromHours(1);

    private static int Signals(MessagingHost host) => host.Services.GetRequiredService<OutboxWakeUp>().Reader.Count;

    /// <summary>Starts the dispatcher and waits until its startup sweep is over and it is parked on the channel.</summary>
    private async Task<MessagingHost> StartParkedAsync(Probe probe)
    {
        await fixture.ResetAsync();
        var sentinel = await fixture.SeedOutboxAsync("Orders");
        var host = await fixture.StartAsync(probe: probe, poll: LongPoll);
        await Wait.UntilDeliveredAsync(fixture, "Orders", sentinel.Id);
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        return host;
    }

    [Fact]
    public async Task ACommitWithOutboxRowsWakesTheDispatcherWithSaveChangesAlone()
    {
        var probe = new Probe();
        await using var host = await StartParkedAsync(probe);
        var clock = Stopwatch.StartNew();

        var id = await host.PublishAsync(new ThingHappened(Guid.NewGuid()));
        await Wait.UntilDeliveredAsync(fixture, "Orders", id);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"delivery took {clock.Elapsed}");
    }

    [Fact]
    public async Task ACommitInsideAnExplicitTransactionWakesTheDispatcherAfterTheCommit()
    {
        var probe = new Probe();
        await using var host = await StartParkedAsync(probe);
        var ct = TestContext.Current.CancellationToken;
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDb>();
        var callsBefore = probe.CallsOf("FirstHandler");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var id = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().Publish(new ThingHappened(Guid.NewGuid()));
        await db.SaveChangesAsync(ct);
        await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        Assert.Equal(callsBefore, probe.CallsOf("FirstHandler"));

        var clock = Stopwatch.StartNew();
        await tx.CommitAsync(ct);
        await Wait.UntilDeliveredAsync(fixture, "Orders", id);

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"delivery took {clock.Elapsed}");
    }

    [Fact]
    public async Task ASaveSignalsOnceAfterAnInternalCommitAndNothingWhenThereAreNoOutboxRows()
    {
        await fixture.ResetAsync();
        await using var host = await fixture.StartAsync(dispatcherEnabled: false);
        var ct = TestContext.Current.CancellationToken;

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDb>();
            db.Things.Add(new Thing { Id = Guid.NewGuid(), Name = "no event" });
            await db.SaveChangesAsync(ct);
        }

        Assert.Equal(0, Signals(host));

        await host.PublishAsync(new ThingHappened(Guid.NewGuid()));

        Assert.Equal(1, Signals(host));
    }

    [Fact]
    public async Task ARolledBackTransactionSendsNoSignalAndALaterCommitSendsOne()
    {
        await fixture.ResetAsync();
        await using var host = await fixture.StartAsync(dispatcherEnabled: false);
        var ct = TestContext.Current.CancellationToken;

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDb>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().Publish(new ThingHappened(Guid.NewGuid()));
            await db.SaveChangesAsync(ct);
            Assert.Equal(0, Signals(host));
            await tx.RollbackAsync(ct);
        }

        Assert.Equal(0, Signals(host));

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDb>();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().Publish(new ThingHappened(Guid.NewGuid()));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        Assert.Equal(1, Signals(host));
    }
}
