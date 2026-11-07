using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Jobs.Tests;

/// <summary>
/// With the periodic safety-net poll set to 1 h it can no longer mask a missing wake-up, so the worker must
/// wake itself for a signal, for a foreign lease that expires after the startup sweep, for a Pending job whose
/// NextAttemptAt is in the future, and after a sweep that failed on a transient database fault.
/// </summary>
public sealed class JobWakeUpTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string Type = "Wake";
    private static readonly TimeSpan LongPoll = TimeSpan.FromHours(1);
    private static readonly TimeSpan ShortLease = TimeSpan.FromSeconds(5);

    /// <summary>Simulates a transient fault (e.g. a database resuming from suspend) during one sweep: once armed,
    /// the next claim query and then the next schedule query each fail exactly once.</summary>
    private sealed class SweepFaultInterceptor : DbCommandInterceptor
    {
        private int _armed;
        private int _claimFaults;
        private int _scheduleFaults;

        public int ClaimFaults => Volatile.Read(ref _claimFaults);

        public int ScheduleFaults => Volatile.Read(ref _scheduleFaults);

        public void Arm() => Volatile.Write(ref _armed, 1);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _armed) == 1)
            {
                var text = command.CommandText;
                if (text.Contains("\"due_at\" IS NULL OR", StringComparison.Ordinal)
                    && Interlocked.CompareExchange(ref _claimFaults, 1, 0) == 0)
                {
                    throw new TimeoutException("Simulated transient DB fault on the claim query.");
                }

                if (text.Contains("LEAST(", StringComparison.Ordinal)
                    && Volatile.Read(ref _claimFaults) == 1
                    && Interlocked.CompareExchange(ref _scheduleFaults, 1, 0) == 0)
                {
                    throw new TimeoutException("Simulated transient DB fault on the schedule query.");
                }
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>Starts the worker and waits until its startup sweep is over and it is parked on the wake-up
    /// channel (a sentinel job is processed, then the loop runs its final empty claim and waits), so a test
    /// can then seed rows the startup sweep can never have seen, however slow the host start is.</summary>
    private async Task<WorkerHost> StartParkedAsync(CountingHandler handler, SweepFaultInterceptor? fault = null)
    {
        await fixture.ResetAsync();
        var sentinel = await fixture.SeedAsync(Type);
        var host = await fixture.StartWorkerAsync(s =>
        {
            s.AddSingleton<IJobHandler>(handler);
            if (fault is not null)
            {
                s.ConfigureDbContext<TestDb>(options => options.AddInterceptors(fault));
            }
        }, ShortLease, LongPoll);

        await Wait.UntilAsync(
            async () => (await fixture.LoadAsync(sentinel.Id)).Status == JobStatus.Completed,
            "sentinel processed, worker past its startup sweep");
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        return host;
    }

    [Fact]
    public async Task SignalTriggersAnImmediateSweepEvenWithALongPollInterval()
    {
        var handler = new CountingHandler(Type);
        await using var host = await StartParkedAsync(handler);
        var job = await fixture.SeedAsync(Type);

        host.Services.GetRequiredService<IJobWakeUp>().Signal();

        await Wait.UntilAsync(
            async () => (await fixture.LoadAsync(job.Id)).Status == JobStatus.Completed,
            "signalled job processed (1 h poll must not be needed)", TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task EnqueueAsyncWakesTheWorkerWithoutThePoll()
    {
        var handler = new CountingHandler(Type);
        await using var host = await StartParkedAsync(handler);

        var group = Guid.NewGuid();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IJobQueue>()
                .EnqueueAsync(group, Type, new { }, TestContext.Current.CancellationToken);
        }

        await Wait.UntilAsync(async () =>
        {
            await using var db = fixture.NewDb();
            return await db.Jobs.AnyAsync(j => j.GroupKey == group && j.Status == JobStatus.Completed, TestContext.Current.CancellationToken);
        }, "enqueued job processed (1 h poll must not be needed)", TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task ForeignLeaseExpiringAfterTheStartupSweepIsRecoveredWithoutThePoll()
    {
        var handler = new CountingHandler(Type);
        await using var host = await StartParkedAsync(handler);

        // Seeded only after the worker parked: a signalled sweep sees the live lease; only the scheduled
        // wake can recover it later (the poll is 1 h).
        var job = await fixture.SeedAsync(
            Type, JobStatus.Running, leaseOwner: "dead-worker", leaseExpiresAt: DateTimeOffset.UtcNow.AddSeconds(2));
        host.Services.GetRequiredService<IJobWakeUp>().Signal();

        await Wait.UntilAsync(
            async () => (await fixture.LoadAsync(job.Id)).Status == JobStatus.Completed,
            "job with an expiring foreign lease recovered and completed (1 h poll must not be needed)");

        Assert.Equal(1, (await fixture.LoadAsync(job.Id)).Attempts);
        Assert.Equal(1, handler.Calls[job.Id]);
    }

    [Fact]
    public async Task PendingJobWithAFutureNextAttemptAtIsProcessedWithoutThePoll()
    {
        var handler = new CountingHandler(Type);
        await using var host = await StartParkedAsync(handler);

        var job = await fixture.SeedAsync(Type, nextAttemptAt: DateTimeOffset.UtcNow.AddSeconds(2), attempts: 1);
        host.Services.GetRequiredService<IJobWakeUp>().Signal(); // the sweep skips the not-yet-due job and must schedule its own wake

        await Wait.UntilAsync(
            async () => (await fixture.LoadAsync(job.Id)).Status == JobStatus.Completed,
            "backed-off Pending job processed shortly after NextAttemptAt (1 h poll must not be needed)");

        Assert.Equal(1, handler.Calls[job.Id]);
    }

    [Fact]
    public async Task FailedSweepAtTheScheduledWakeStillProcessesTheDueJobWithoutThePoll()
    {
        var handler = new CountingHandler(Type);
        var fault = new SweepFaultInterceptor();
        await using var host = await StartParkedAsync(handler, fault);

        var job = await fixture.SeedAsync(Type, nextAttemptAt: DateTimeOffset.UtcNow.AddSeconds(2), attempts: 1);
        host.Services.GetRequiredService<IJobWakeUp>().Signal(); // healthy sweep: skips the not-yet-due job, schedules the wake
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        fault.Arm(); // the sweep at the scheduled wake hits a transient fault (claim + schedule)

        await Wait.UntilAsync(
            async () => (await fixture.LoadAsync(job.Id)).Status == JobStatus.Completed,
            "due job processed although the sweep at its scheduled wake failed (1 h poll must not be needed)",
            TimeSpan.FromSeconds(30));

        Assert.Equal(1, fault.ClaimFaults);
        Assert.Equal(1, fault.ScheduleFaults);
        Assert.Equal(1, handler.Calls[job.Id]);
    }
}
