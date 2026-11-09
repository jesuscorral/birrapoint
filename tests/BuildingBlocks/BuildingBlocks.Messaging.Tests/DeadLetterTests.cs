using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging.Tests;

/// <summary>A delivery that keeps failing is retried with growing delays, then ends Dead and visible in the logs,
/// without blocking later events (FR-080, SC-022).</summary>
public sealed class DeadLetterTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string SecretText = "SECRET-PAYLOAD-TEXT";

    [Fact]
    public async Task AnAlwaysFailingSubscriberIsRetriedThenDeadWithOneErrorLogAndNoPayload()
    {
        await fixture.ResetAsync();
        using var logs = new CapturingLoggerProvider();
        var probe = new Probe { OnHandle = (_, _, _) => throw new InvalidOperationException("subscriber unavailable") };
        await using var host = await fixture.StartAsync(
            m =>
            {
                m.Publishes<OtherHappened>("Billing");
                m.Subscribe<OtherHappened, OtherHandler>("Orders");
            },
            probe: probe,
            maxAttempts: 3,
            logs: logs);

        var id = await host.PublishAsync(new OtherHappened(SecretText));

        await Wait.UntilAsync(
            async () => (await fixture.OutboxAsync("Billing", id)).Status == OutboxStatus.Dead,
            "event ends Dead after the last attempt");
        var row = await fixture.OutboxAsync("Billing", id);
        Assert.Equal(3, probe.CallsOf("OtherHandler"));
        Assert.Equal(3, row.Attempts);
        Assert.Contains("OtherHandler", row.LastError, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretText, row.LastError, StringComparison.Ordinal);
        Assert.Null(row.LeaseOwner);

        var errors = logs.Entries.Where(e => e.Level == LogLevel.Error && e.Message.Contains(id.ToString(), StringComparison.Ordinal)).ToList();
        var error = Assert.Single(errors);
        Assert.Contains("test.other-happened", error.Message, StringComparison.Ordinal);
        Assert.Contains("OtherHandler", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains(SecretText, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RetriesAreSpacedByAGrowingBackoff()
    {
        await fixture.ResetAsync();
        var seen = new List<(int Attempts, DateTimeOffset? Next)>();
        var probe = new Probe { OnHandle = (_, _, _) => throw new InvalidOperationException("nope") };
        await using var host = await fixture.StartAsync(
            m =>
            {
                m.Publishes<OtherHappened>("Billing");
                m.Subscribe<OtherHappened, OtherHandler>("Orders");
            },
            probe: probe,
            maxAttempts: 4,
            maxBackoff: TimeSpan.FromSeconds(4));

        var id = await host.PublishAsync(new OtherHappened("x"));

        await Wait.UntilAsync(
            async () =>
            {
                var row = await fixture.OutboxAsync("Billing", id);
                if (row.NextAttemptAt is not null && (seen.Count == 0 || seen[^1].Attempts != row.Attempts))
                {
                    seen.Add((row.Attempts, row.NextAttemptAt));
                }

                return row.Status == OutboxStatus.Dead;
            },
            "event ends Dead");

        Assert.Equal([1, 2, 3], seen.Select(s => s.Attempts));
        Assert.All(seen, s => Assert.NotNull(s.Next));
    }

    [Fact]
    public async Task AnUnknownTypeGoesStraightToDead()
    {
        await fixture.ResetAsync();
        using var logs = new CapturingLoggerProvider();
        await using var host = await fixture.StartAsync(logs: logs);
        var unknown = await fixture.SeedOutboxAsync("Orders", type: "test.never-registered");

        await Wait.UntilAsync(
            async () => (await fixture.OutboxAsync("Orders", unknown.Id)).Status == OutboxStatus.Dead,
            "unknown type is dead at once");

        var row = await fixture.OutboxAsync("Orders", unknown.Id);
        Assert.Equal(1, row.Attempts);
        Assert.Contains("test.never-registered", row.LastError, StringComparison.Ordinal);
        Assert.Single(logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains(unknown.Id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task EventsPublishedAfterADeadOneAreStillDelivered()
    {
        await fixture.ResetAsync();
        var probe = new Probe();
        await using var host = await fixture.StartAsync(probe: probe);
        var dead = await fixture.SeedOutboxAsync("Orders", type: "test.never-registered");
        await Wait.UntilAsync(
            async () => (await fixture.OutboxAsync("Orders", dead.Id)).Status == OutboxStatus.Dead,
            "dead event parked");

        var thingId = Guid.NewGuid();
        var id = await host.PublishAsync(new ThingHappened(thingId));

        await Wait.UntilDeliveredAsync(fixture, "Orders", id);
        Assert.Equal(OutboxStatus.Dead, (await fixture.OutboxAsync("Orders", dead.Id)).Status);
        Assert.Equal(1, probe.CallsOf("FirstHandler"));
    }
}
