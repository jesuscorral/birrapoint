using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Jobs.Tests;

/// <summary>
/// Claim, lease and recovery semantics of the hosted worker against a real PostgreSQL whose table and columns
/// carry custom names: jobs are claimed atomically (FOR UPDATE SKIP LOCKED + lease), only expired leases are
/// resumed, and an outcome is only written by the worker that still owns the lease.
/// </summary>
public sealed class JobWorkerTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string Type = "Work";

    [Fact]
    public async Task TwoConcurrentWorkersHandleEveryPendingJobExactlyOnce()
    {
        await fixture.ResetAsync();
        var handler = new CountingHandler(Type, delay: TimeSpan.FromMilliseconds(50));
        var jobs = new List<Job>();
        for (var i = 0; i < 20; i++)
        {
            jobs.Add(await fixture.SeedAsync(Type));
        }

        await using var first = await fixture.StartWorkerAsync(s => s.AddSingleton<IJobHandler>(handler));
        await using var second = await fixture.StartWorkerAsync(s => s.AddSingleton<IJobHandler>(handler));

        await Wait.UntilAsync(async () =>
        {
            await using var db = fixture.NewDb();
            return !await db.Jobs.AnyAsync(j => j.Status != JobStatus.Completed, TestContext.Current.CancellationToken);
        }, "all 20 jobs completed");

        Assert.Equal(20, handler.Calls.Count);
        Assert.All(jobs, job => Assert.Equal(1, handler.Calls[job.Id]));
        foreach (var job in jobs)
        {
            Assert.Equal(0, (await fixture.LoadAsync(job.Id)).Attempts);
        }
    }

    [Fact]
    public async Task LongHandlerRenewsItsLeaseSoASweepingWorkerNeverRerunsIt()
    {
        await fixture.ResetAsync();
        var handler = new CountingHandler(Type, delay: TimeSpan.FromSeconds(3.5));
        var job = await fixture.SeedAsync(Type);

        // A 1 s lease would expire several times over the 3.5 s handler without renewal, and the second
        // worker's 1 s sweeps would then recover and re-run the job.
        var lease = TimeSpan.FromSeconds(1);
        await using var owner = await fixture.StartWorkerAsync(s => s.AddSingleton<IJobHandler>(handler), lease, lease);
        await using var sweeper = await fixture.StartWorkerAsync(s => s.AddSingleton<IJobHandler>(handler), lease, lease);

        await Wait.UntilAsync(
            async () => (await fixture.LoadAsync(job.Id)).Status == JobStatus.Completed, "long job completed");

        Assert.Equal(1, handler.Calls[job.Id]);
        Assert.Equal(0, (await fixture.LoadAsync(job.Id)).Attempts);
    }

    [Fact]
    public async Task ExpiredLeaseIsRecoveredThroughTheRetryPathAndReprocessed()
    {
        await fixture.ResetAsync();
        var handler = new CountingHandler(Type);
        var observer = new RecordingObserver();
        var job = await fixture.SeedAsync(
            Type, JobStatus.Running, leaseOwner: "dead-worker", leaseExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        await using var worker = await fixture.StartWorkerAsync(s =>
        {
            s.AddSingleton<IJobHandler>(handler);
            s.AddSingleton<IJobObserver>(observer);
        });

        await Wait.UntilAsync(
            async () => (await fixture.LoadAsync(job.Id)).Status == JobStatus.Completed, "expired-lease job recovered and completed");

        var after = await fixture.LoadAsync(job.Id);
        Assert.Equal(1, after.Attempts); // the interrupted attempt counted once
        Assert.Contains("lease expired", after.LastError, StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls[job.Id]);
        Assert.Null(after.LeaseOwner);
        Assert.Contains(observer.Changes, c => c.JobId == job.Id && c.Status == JobStatus.Pending
            && c.Detail!.Contains("lease expired", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunningJobWithALiveForeignLeaseIsLeftAlone()
    {
        await fixture.ResetAsync();
        var handler = new CountingHandler(Type);
        var foreign = await fixture.SeedAsync(
            Type, JobStatus.Running, leaseOwner: "other-worker", leaseExpiresAt: DateTimeOffset.UtcNow.AddHours(1));
        var sentinel = await fixture.SeedAsync(Type);

        await using var worker = await fixture.StartWorkerAsync(s => s.AddSingleton<IJobHandler>(handler));
        await Wait.UntilAsync(
            async () => (await fixture.LoadAsync(sentinel.Id)).Status == JobStatus.Completed, "a full sweep ran");

        var after = await fixture.LoadAsync(foreign.Id);
        Assert.Equal(JobStatus.Running, after.Status);
        Assert.Equal("other-worker", after.LeaseOwner);
        Assert.Equal(0, after.Attempts);
        Assert.False(handler.Calls.ContainsKey(foreign.Id));
    }

    [Fact]
    public async Task FreshRunningJobWithNoLeaseIsLeftAlone()
    {
        await fixture.ResetAsync();
        var handler = new CountingHandler(Type);
        var fresh = await fixture.SeedAsync(Type, JobStatus.Running, updatedAt: DateTimeOffset.UtcNow);
        var sentinel = await fixture.SeedAsync(Type);

        await using var worker = await fixture.StartWorkerAsync(s => s.AddSingleton<IJobHandler>(handler));
        await Wait.UntilAsync(
            async () => (await fixture.LoadAsync(sentinel.Id)).Status == JobStatus.Completed, "a full sweep ran");

        var after = await fixture.LoadAsync(fresh.Id);
        Assert.Equal(JobStatus.Running, after.Status);
        Assert.Equal(0, after.Attempts);
    }

    [Fact]
    public async Task StaleRunningJobWithNoLeaseIsRecovered()
    {
        await fixture.ResetAsync();
        var handler = new CountingHandler(Type);
        var stale = await fixture.SeedAsync(Type, JobStatus.Running, updatedAt: DateTimeOffset.UtcNow.AddMinutes(-10));

        await using var worker = await fixture.StartWorkerAsync(s => s.AddSingleton<IJobHandler>(handler));

        await Wait.UntilAsync(
            async () => (await fixture.LoadAsync(stale.Id)).Status == JobStatus.Completed, "stale lease-less job recovered");
        Assert.Equal(1, (await fixture.LoadAsync(stale.Id)).Attempts);
    }

    [Fact]
    public async Task LostLeaseDiscardsTheFinishingWorkersOutcome()
    {
        await fixture.ResetAsync();
        var observer = new RecordingObserver();
        var handlerDone = new TaskCompletionSource();
        var job = await fixture.SeedAsync(Type);
        var handler = new DelegateHandler(Type, async (running, ct) =>
        {
            // Another worker recovered the job while this one was still running it.
            await using var db = fixture.NewDb();
            await db.Jobs.Where(j => j.Id == running.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseOwner, "new-owner"), ct);
            handlerDone.SetResult();
        });

        await using var worker = await fixture.StartWorkerAsync(s =>
        {
            s.AddSingleton<IJobHandler>(handler);
            s.AddSingleton<IJobObserver>(observer);
        });

        await handlerDone.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);

        var after = await fixture.LoadAsync(job.Id);
        Assert.Equal(JobStatus.Running, after.Status);
        Assert.Equal("new-owner", after.LeaseOwner);
        Assert.Empty(observer.Changes); // a discarded outcome is not announced
    }

    [Fact]
    public async Task FailingHandlerIsRetriedWithBackoffThenMarkedFailedAfterMaxAttempts()
    {
        await fixture.ResetAsync();
        var calls = new List<DateTimeOffset>();
        var job = await fixture.SeedAsync(Type);
        var handler = new DelegateHandler(Type, (_, _) =>
        {
            lock (calls)
            {
                calls.Add(DateTimeOffset.UtcNow);
            }

            throw new InvalidOperationException("boom");
        });

        await using var worker = await fixture.StartWorkerAsync(
            s => s.AddSingleton<IJobHandler>(handler), maxAttempts: 2);

        await Wait.UntilAsync(
            async () => (await fixture.LoadAsync(job.Id)).Status == JobStatus.Failed, "job failed after max attempts",
            TimeSpan.FromSeconds(30));

        var after = await fixture.LoadAsync(job.Id);
        Assert.Equal(2, after.Attempts);
        Assert.Equal("boom", after.LastError);
        Assert.Null(after.LeaseOwner);
        Assert.Null(after.LeaseExpiresAt);
        Assert.Equal(2, calls.Count);
        Assert.True(calls[1] - calls[0] >= TimeSpan.FromSeconds(1.8), $"backoff was {calls[1] - calls[0]}");
    }

    [Fact]
    public async Task LastErrorIsTruncatedToTheColumnLength()
    {
        await fixture.ResetAsync();
        var job = await fixture.SeedAsync(Type);
        var handler = new DelegateHandler(Type, (_, _) => throw new InvalidOperationException(new string('x', 3000)));

        await using var worker = await fixture.StartWorkerAsync(s => s.AddSingleton<IJobHandler>(handler), maxAttempts: 1);

        await Wait.UntilAsync(async () => (await fixture.LoadAsync(job.Id)).Status == JobStatus.Failed, "job failed");
        Assert.Equal(2000, (await fixture.LoadAsync(job.Id)).LastError!.Length);
    }

    [Fact]
    public async Task JobWithoutARegisteredHandlerFailsThroughTheRetryPath()
    {
        await fixture.ResetAsync();
        var job = await fixture.SeedAsync("Unhandled");

        await using var worker = await fixture.StartWorkerAsync(maxAttempts: 1);

        await Wait.UntilAsync(async () => (await fixture.LoadAsync(job.Id)).Status == JobStatus.Failed, "job failed");
        Assert.Contains("Unhandled", (await fixture.LoadAsync(job.Id)).LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompletedOutcomeIsRecordedWithTheLeaseCleared()
    {
        await fixture.ResetAsync();
        var handler = new CountingHandler(Type);
        var job = await fixture.SeedAsync(Type);
        var before = (await fixture.LoadAsync(job.Id)).UpdatedAt;

        await using var worker = await fixture.StartWorkerAsync(s => s.AddSingleton<IJobHandler>(handler));
        await Wait.UntilAsync(async () => (await fixture.LoadAsync(job.Id)).Status == JobStatus.Completed, "job completed");

        var after = await fixture.LoadAsync(job.Id);
        Assert.Null(after.LeaseOwner);
        Assert.Null(after.LeaseExpiresAt);
        Assert.True(after.UpdatedAt > before);
    }
}
