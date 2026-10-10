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

    private AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.ConnectionString).Options);

    /// <summary>Derives a full API host (own DispatchWorker, own wake-up channel) on the shared
    /// database whose only job handler is <paramref name="handler"/>.</summary>
    private WebApplicationFactory<Program> NewWorkerHost(CountingHandler handler) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Dispatch:LeaseDuration"] = "00:00:05" }));
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

        Assert.Equal(jobCount, handler.Calls.Count);
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
        _ = host.Services; // starts the worker: ResumeInterruptedJobsAsync runs first
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
}
