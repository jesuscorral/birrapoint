using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging.Tests;

/// <summary>One purge pass removes only rows past their retention, in batches, and runs at most once per interval
/// (FR-090, SC-024). A hand-moved clock stands in for days passing; the dispatcher is off so only the purger acts.</summary>
public sealed class RetentionPurgeTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private async Task<(MessagingHost Host, FakeTimeProvider Clock, CapturingLoggerProvider Logs)> StartAsync(int batchSize = 1000)
    {
        await fixture.ResetAsync();
        var clock = new FakeTimeProvider(Start);
        var logs = new CapturingLoggerProvider();
        var host = await fixture.StartAsync(
            services: s =>
            {
                s.AddSingleton<TimeProvider>(clock);
                s.Configure<MessagingOptions>(o => o.PurgeBatchSize = batchSize);
            },
            dispatcherEnabled: false,
            logs: logs);
        return (host, clock, logs);
    }

    private static Task<bool> PurgeAsync(MessagingHost host) =>
        host.Services.GetRequiredService<RetentionPurger<TestDb>>().RunIfDueAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task OnePassRemovesRowsPastRetentionAndKeepsEverythingElse()
    {
        var (host, _, logs) = await StartAsync();
        await using var hostScope = host;
        using var logScope = logs;
        var old = Start - TimeSpan.FromDays(40);
        var delivered8 = await fixture.SeedOutboxAsync("Orders", status: OutboxStatus.Delivered, occurredAt: Start - TimeSpan.FromDays(8));
        var delivered6 = await fixture.SeedOutboxAsync("Orders", status: OutboxStatus.Delivered, occurredAt: Start - TimeSpan.FromDays(6));
        var dead31 = await fixture.SeedOutboxAsync("Billing", status: OutboxStatus.Dead, occurredAt: Start - TimeSpan.FromDays(31));
        var dead29 = await fixture.SeedOutboxAsync("Billing", status: OutboxStatus.Dead, occurredAt: Start - TimeSpan.FromDays(29));
        var pending = await fixture.SeedOutboxAsync("Orders", status: OutboxStatus.Pending, occurredAt: old);
        var delivering = await fixture.SeedOutboxAsync("Orders", status: OutboxStatus.Delivering, occurredAt: old, leaseOwner: "x", leaseExpiresAt: Start);
        var oldEvent = Guid.NewGuid();
        var youngEvent = Guid.NewGuid();
        await fixture.SeedInboxAsync("Shipping", oldEvent, "FirstHandler", Start - TimeSpan.FromDays(31));
        await fixture.SeedInboxAsync("Orders", oldEvent, "SecondHandler", Start - TimeSpan.FromDays(31));
        await fixture.SeedInboxAsync("Shipping", youngEvent, "FirstHandler", Start - TimeSpan.FromDays(29));

        Assert.True(await PurgeAsync(host));

        Assert.Equal(
            new[] { delivering.Id, delivered6.Id, pending.Id }.Order(),
            (await fixture.OutboxRowsAsync("Orders")).Select(r => r.Id).Order());
        Assert.Equal(dead29.Id, Assert.Single(await fixture.OutboxRowsAsync("Billing")).Id);
        Assert.Equal(youngEvent, Assert.Single(await fixture.InboxRowsAsync("Shipping")).EventId);
        Assert.Empty(await fixture.InboxRowsAsync("Orders"));
        Assert.DoesNotContain(await fixture.OutboxRowsAsync("Orders"), r => r.Id == delivered8.Id);
        Assert.DoesNotContain(await fixture.OutboxRowsAsync("Billing"), r => r.Id == dead31.Id);
    }

    [Fact]
    public async Task ABatchSmallerThanTheBacklogStillClearsEverything()
    {
        var (host, _, logs) = await StartAsync(batchSize: 2);
        await using var hostScope = host;
        using var logScope = logs;
        for (var i = 0; i < 5; i++)
        {
            await fixture.SeedOutboxAsync("Orders", status: OutboxStatus.Delivered, occurredAt: Start - TimeSpan.FromDays(10));
            await fixture.SeedInboxAsync("Shipping", Guid.NewGuid(), "FirstHandler", Start - TimeSpan.FromDays(31));
        }

        await PurgeAsync(host);

        Assert.Empty(await fixture.OutboxRowsAsync("Orders"));
        Assert.Empty(await fixture.InboxRowsAsync("Shipping"));
    }

    [Fact]
    public async Task ThePassLogsTheCountsPerTable()
    {
        var (host, _, logs) = await StartAsync();
        await using var hostScope = host;
        using var logScope = logs;
        await fixture.SeedOutboxAsync("Orders", status: OutboxStatus.Delivered, occurredAt: Start - TimeSpan.FromDays(10));
        await fixture.SeedOutboxAsync("Orders", status: OutboxStatus.Delivered, occurredAt: Start - TimeSpan.FromDays(10));
        await fixture.SeedInboxAsync("Shipping", Guid.NewGuid(), "FirstHandler", Start - TimeSpan.FromDays(31));

        await PurgeAsync(host);

        var messages = logs.Entries.Where(e => e.Level == LogLevel.Information).Select(e => e.Message).ToList();
        Assert.Contains(messages, m => m.Contains("orders.OutboxMessages", StringComparison.Ordinal) && m.Contains("2 delivered", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("shipping.InboxMessages", StringComparison.Ordinal) && m.Contains('1', StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("billing.OutboxMessages", StringComparison.Ordinal) && m.Contains("0 delivered", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASecondPassWithinTheIntervalDoesNothingAndALaterOneRuns()
    {
        var (host, clock, logs) = await StartAsync();
        await using var hostScope = host;
        using var logScope = logs;
        Assert.True(await PurgeAsync(host));
        await fixture.SeedOutboxAsync("Orders", status: OutboxStatus.Delivered, occurredAt: Start - TimeSpan.FromDays(10));

        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.False(await PurgeAsync(host));
        Assert.Single(await fixture.OutboxRowsAsync("Orders"));

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(await PurgeAsync(host));
        Assert.Empty(await fixture.OutboxRowsAsync("Orders"));
    }
}
