using System.Collections.Concurrent;
using System.Threading.Channels;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BirraPoint.Api.IntegrationTests.Dispatch;

/// <summary>
/// T129 (PR #46 review M3): DispatchJob must be safe under overlapping API revisions. Several
/// API hosts (each with its own DispatchWorker) share one Postgres; jobs are claimed atomically
/// (FOR UPDATE SKIP LOCKED + lease) and only expired leases are resumed. The shared base
/// ApiFactory host is never started here (its worker would run the real handlers): jobs are
/// seeded straight into the container and extra hosts are derived with a counting handler.
/// </summary>
public sealed class DispatchConcurrencyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Counts handler invocations per job id; optional delay widens the race window.</summary>
    private sealed class CountingHandler(TimeSpan delay) : IDispatchJobHandler
    {
        public ConcurrentDictionary<Guid, int> Calls { get; } = new();

        public DispatchJobType Type => DispatchJobType.SendInvitation;

        public async Task HandleAsync(DispatchJob job, CancellationToken cancellationToken)
        {
            Calls.AddOrUpdate(job.Id, 1, (_, n) => n + 1);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>Blocks the gated job's handler until released, so a test can act mid-handler.</summary>
    private sealed class GatedHandler : IDispatchJobHandler
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid GatedJobId { get; set; }

        public Task Started => _started.Task;

        public ConcurrentDictionary<Guid, int> Calls { get; } = new();

        public DispatchJobType Type => DispatchJobType.SendInvitation;

        public void Release() => _release.TrySetResult();

        public async Task HandleAsync(DispatchJob job, CancellationToken cancellationToken)
        {
            Calls.AddOrUpdate(job.Id, 1, (_, n) => n + 1);
            if (job.Id == GatedJobId)
            {
                _started.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.ConnectionString).Options);

    /// <summary>Derives a full API host (own DispatchWorker, own wake-up channel) on the shared
    /// database whose only job handler is <paramref name="handler"/>.</summary>
    private WebApplicationFactory<Program> NewWorkerHost(IDispatchJobHandler handler, string leaseDuration = "00:00:05") =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Dispatch:LeaseDuration"] = leaseDuration }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDispatchJobHandler>();
                services.AddSingleton<IDispatchJobHandler>(handler);
            });
        });

    private async Task<Guid> SeedCompetitionAsync()
    {
        await using var db = NewDb();
        var competition = new Competition
        {
            Name = $"Lease {Guid.NewGuid():N}",
            Venue = "Venue",
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 3),
            CreatedByUserId = "organizer-lease",
        };
        db.Competitions.Add(competition);
        await db.SaveChangesAsync();
        return competition.Id;
    }

    private async Task<DispatchJob> SeedJobAsync(
        Guid competitionId, DispatchJobStatus status = DispatchJobStatus.Pending,
        string? leaseOwner = null, DateTimeOffset? leaseExpiresAt = null)
    {
        await using var db = NewDb();
        var job = new DispatchJob
        {
            CompetitionId = competitionId,
            Type = DispatchJobType.SendInvitation,
            PayloadJson = "{}",
            Status = status,
            LeaseOwner = leaseOwner,
            LeaseExpiresAt = leaseExpiresAt,
        };
        db.DispatchJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private async Task<DispatchJob> LoadAsync(Guid jobId)
    {
        await using var db = NewDb();
        return await db.DispatchJobs.AsNoTracking().SingleAsync(j => j.Id == jobId);
    }

    private static void Wake(WebApplicationFactory<Program> host, Guid jobId) =>
        Assert.True(host.Services.GetRequiredService<Channel<Guid>>().Writer.TryWrite(jobId));

    private async Task BackdateUpdatedAtAsync(Guid jobId, TimeSpan age)
    {
        await using var db = NewDb();
        var stamp = DateTimeOffset.UtcNow - age;
        await db.DispatchJobs.Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.UpdatedAt, stamp), TestContext.Current.CancellationToken);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string because)
    {
        using var cts = new CancellationTokenSource(PollTimeout);
        while (!await condition())
        {
            if (cts.IsCancellationRequested)
            {
                Assert.Fail($"Timed out after {PollTimeout}: {because}");
            }

            await Task.Delay(PollInterval);
        }
    }

    [Fact]
    public async Task TwoConcurrentWorkers_HandleEveryPendingJobExactlyOnce()
    {
        const int jobCount = 20;
        var competitionId = await SeedCompetitionAsync();
        var handler = new CountingHandler(delay: TimeSpan.FromMilliseconds(100));

        await using var hostA = NewWorkerHost(handler);
        await using var hostB = NewWorkerHost(handler);
        _ = hostA.Services;
        _ = hostB.Services;

        var jobIds = new List<Guid>();
        for (var i = 0; i < jobCount; i++)
        {
            jobIds.Add((await SeedJobAsync(competitionId)).Id);
        }

        // Wake both workers at once so their claim sweeps overlap.
        Wake(hostA, jobIds[0]);
        Wake(hostB, jobIds[0]);

        await WaitUntilAsync(async () =>
        {
            await using var db = NewDb();
            return await db.DispatchJobs.CountAsync(
                j => j.CompetitionId == competitionId && j.Status == DispatchJobStatus.Completed) == jobCount;
        }, "all jobs Completed");

        // The hosts share the database with the other tests, whose leftover Running jobs (e.g. a
        // null-lease job that turns stale) these workers may legitimately recover: count only
        // this test's jobs.
        Assert.Equal(jobCount, handler.Calls.Keys.Count(jobIds.Contains));
        Assert.All(jobIds, id => Assert.Equal(1, handler.Calls[id]));

        await using var verify = NewDb();
        var jobs = await verify.DispatchJobs.AsNoTracking().Where(j => j.CompetitionId == competitionId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.All(jobs, j => Assert.Equal(0, j.Attempts));
    }

    [Fact]
    public async Task StartingWorker_LeavesRunningJobWithLiveForeignLeaseAlone()
    {
        var competitionId = await SeedCompetitionAsync();
        var leaseExpiry = DateTimeOffset.UtcNow.AddHours(1);
        var running = await SeedJobAsync(competitionId, DispatchJobStatus.Running, "other-revision-worker", leaseExpiry);
        var sentinel = await SeedJobAsync(competitionId);
        var handler = new CountingHandler(TimeSpan.Zero);

        await using var host = NewWorkerHost(handler);
        _ = host.Services; // starts the worker: RecoverExpiredLeasesAsync runs first
        Wake(host, sentinel.Id);

        // The sentinel completing proves the worker finished its startup resume and a full
        // processing cycle; only then is "the foreign job was left alone" a meaningful assertion.
        await WaitUntilAsync(
            async () => (await LoadAsync(sentinel.Id)).Status == DispatchJobStatus.Completed, "sentinel Completed");

        var after = await LoadAsync(running.Id);
        Assert.Equal(DispatchJobStatus.Running, after.Status);
        Assert.Equal(0, after.Attempts);
        Assert.Equal("other-revision-worker", after.LeaseOwner);
        Assert.Equal(leaseExpiry.ToUnixTimeMilliseconds(), after.LeaseExpiresAt!.Value.ToUnixTimeMilliseconds());
        Assert.False(handler.Calls.ContainsKey(running.Id));
    }

    [Fact]
    public async Task StartingWorker_RecoversRunningJobWhoseLeaseExpired()
    {
        var competitionId = await SeedCompetitionAsync();
        var running = await SeedJobAsync(
            competitionId, DispatchJobStatus.Running, "crashed-worker", DateTimeOffset.UtcNow.AddMinutes(-1));
        var handler = new CountingHandler(TimeSpan.Zero);

        await using var host = NewWorkerHost(handler);
        _ = host.Services;

        // Recovery goes through the retry path (Attempts++, backoff) and the job is then
        // claimed and processed by the new worker.
        await WaitUntilAsync(
            async () => (await LoadAsync(running.Id)).Status == DispatchJobStatus.Completed,
            "expired-lease job recovered and Completed");

        var after = await LoadAsync(running.Id);
        Assert.Equal(1, after.Attempts);
        Assert.Equal(1, handler.Calls[running.Id]);
    }

    [Fact]
    public async Task StartingWorker_LeavesFreshRunningJobWithNullLeaseAlone()
    {
        // A pre-T129 revision marks jobs Running without a lease while its handler is mid-flight.
        var competitionId = await SeedCompetitionAsync();
        var running = await SeedJobAsync(competitionId, DispatchJobStatus.Running);
        await BackdateUpdatedAtAsync(running.Id, TimeSpan.Zero);
        var sentinel = await SeedJobAsync(competitionId);
        var handler = new CountingHandler(TimeSpan.Zero);

        await using var host = NewWorkerHost(handler);
        _ = host.Services;
        Wake(host, sentinel.Id);

        await WaitUntilAsync(
            async () => (await LoadAsync(sentinel.Id)).Status == DispatchJobStatus.Completed, "sentinel Completed");

        var after = await LoadAsync(running.Id);
        Assert.Equal(DispatchJobStatus.Running, after.Status);
        Assert.Equal(0, after.Attempts);
        Assert.False(handler.Calls.ContainsKey(running.Id));
    }

    [Fact]
    public async Task StartingWorker_RecoversStaleRunningJobWithNullLease()
    {
        var competitionId = await SeedCompetitionAsync();
        var running = await SeedJobAsync(competitionId, DispatchJobStatus.Running);
        await BackdateUpdatedAtAsync(running.Id, TimeSpan.FromMinutes(1)); // older than the 5 s lease
        var handler = new CountingHandler(TimeSpan.Zero);

        await using var host = NewWorkerHost(handler);
        _ = host.Services;

        await WaitUntilAsync(
            async () => (await LoadAsync(running.Id)).Status == DispatchJobStatus.Completed,
            "stale null-lease job recovered and Completed");

        var after = await LoadAsync(running.Id);
        Assert.Equal(1, after.Attempts);
        Assert.Equal(1, handler.Calls[running.Id]);
    }

    [Fact]
    public async Task LongHandler_RenewsLease_SoSweepingWorkerNeverRerunsIt()
    {
        var competitionId = await SeedCompetitionAsync();
        var handler = new CountingHandler(delay: TimeSpan.FromSeconds(3)); // 3x the 1 s lease

        await using var hostA = NewWorkerHost(handler, "00:00:01");
        await using var hostB = NewWorkerHost(handler, "00:00:01");
        _ = hostA.Services;
        _ = hostB.Services;

        var job = await SeedJobAsync(competitionId);
        Wake(hostA, job.Id);

        // Keep host B sweeping (recovery runs on every wake-up) for the whole handler duration.
        await WaitUntilAsync(async () =>
        {
            hostB.Services.GetRequiredService<Channel<Guid>>().Writer.TryWrite(job.Id);
            return (await LoadAsync(job.Id)).Status == DispatchJobStatus.Completed;
        }, "long job Completed");

        var after = await LoadAsync(job.Id);
        Assert.Equal(0, after.Attempts);
        Assert.Equal(1, handler.Calls[job.Id]);
    }

    [Fact]
    public async Task LostLease_DiscardsFinishingWorkersOutcome()
    {
        var competitionId = await SeedCompetitionAsync();
        var job = await SeedJobAsync(competitionId);
        var sentinel = await SeedJobAsync(competitionId);
        var handler = new GatedHandler { GatedJobId = job.Id };

        await using var host = NewWorkerHost(handler);
        _ = host.Services;
        Wake(host, job.Id);
        await handler.Started.WaitAsync(PollTimeout, TestContext.Current.CancellationToken);

        // Another worker "takes over": different owner, lease kept live.
        var farFuture = DateTimeOffset.UtcNow.AddHours(1);
        await using (var db = NewDb())
        {
            var rows = await db.DispatchJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(
                s => s.SetProperty(j => j.LeaseOwner, "thief").SetProperty(j => j.LeaseExpiresAt, farFuture),
                TestContext.Current.CancellationToken);
            Assert.Equal(1, rows);
        }

        handler.Release();

        // The worker is sequential: the sentinel completing proves it already tried to finish the gated job.
        await WaitUntilAsync(
            async () => (await LoadAsync(sentinel.Id)).Status == DispatchJobStatus.Completed, "sentinel Completed");

        var after = await LoadAsync(job.Id);
        Assert.Equal(DispatchJobStatus.Running, after.Status);
        Assert.Equal("thief", after.LeaseOwner);
        Assert.Equal(0, after.Attempts);
    }
}
