using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using BirraPoint.Api.Common.Email;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Dispatch;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BirraPoint.Api.IntegrationTests.Dispatch;

/// <summary>
/// T132 (PR #67 review M1/M3): with the periodic safety-net poll set to 1 h it can no longer mask
/// a missing wake-up, so the worker must wake itself for (a) a foreign lease that expires after
/// the startup sweep and (b) a Pending job whose NextAttemptAt is in the future when the process
/// starts (its in-process retry signal died with the previous process), and (c) RetryDispatch
/// must signal the worker after resetting Failed jobs.
/// </summary>
public sealed class DispatchWakeUpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private sealed class CountingHandler : IDispatchJobHandler
    {
        public ConcurrentDictionary<Guid, int> Calls { get; } = new();

        public DispatchJobType Type => DispatchJobType.SendInvitation;

        public Task HandleAsync(DispatchJob job, CancellationToken cancellationToken)
        {
            Calls.AddOrUpdate(job.Id, 1, (_, n) => n + 1);
            return Task.CompletedTask;
        }
    }

    /// <summary>Simulates a transient DB fault (e.g. Neon resuming from suspend) during one sweep:
    /// once armed, the next claim query and then the next schedule query each fail exactly once.
    /// Inert until <see cref="Arm"/> is called.</summary>
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
                if (text.Contains("\"NextAttemptAt\" IS NULL OR", StringComparison.Ordinal)
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

    private AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.ConnectionString).Options);

    private WebApplicationFactory<Program> NewHost(
        IDispatchJobHandler? handler = null, SweepFaultInterceptor? fault = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Dispatch:LeaseDuration"] = "00:00:05",
                    ["Dispatch:SafetyNetPollInterval"] = "01:00:00",
                }));
            builder.ConfigureServices(services =>
            {
                if (handler is not null)
                {
                    services.RemoveAll<IDispatchJobHandler>();
                    services.AddSingleton(handler);
                }

                if (fault is not null)
                {
                    services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(fault));
                }
            });
        });

    /// <summary>Starts the host and waits until its startup sweep is over and the worker is parked
    /// on the wake-up channel (a sentinel job is processed, then the loop runs its final empty claim
    /// and waits), so a test can then seed rows the startup sweep can never have seen, however slow
    /// the host start is.</summary>
    private async Task ParkWorkerAsync(WebApplicationFactory<Program> host, Guid competitionId)
    {
        var sentinel = await SeedJobAsync(competitionId, DispatchJobStatus.Pending);
        _ = host.Services;
        await WaitUntilAsync(
            async () => (await LoadAsync(sentinel.Id)).Status == DispatchJobStatus.Completed,
            "sentinel processed, worker past its startup sweep");
        // Let the sweep loop run its final empty claim, schedule and park (no observable signal).
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    }

    private static void Signal(WebApplicationFactory<Program> host) =>
        Assert.True(host.Services.GetRequiredService<Channel<Guid>>().Writer.TryWrite(Guid.Empty));

    private async Task<Guid> SeedCompetitionAsync()
    {
        await using var db = NewDb();
        var competition = new Competition
        {
            Name = $"WakeUp {Guid.NewGuid():N}",
            Venue = "Venue",
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 3),
            CreatedByUserId = "organizer-wakeup",
        };
        db.Competitions.Add(competition);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return competition.Id;
    }

    private async Task<DispatchJob> SeedJobAsync(
        Guid competitionId, DispatchJobStatus status, string? leaseOwner = null,
        DateTimeOffset? leaseExpiresAt = null, DateTimeOffset? nextAttemptAt = null,
        DispatchJobType type = DispatchJobType.SendInvitation, string payloadJson = "{}",
        int attempts = 0, string? lastError = null)
    {
        await using var db = NewDb();
        var job = new DispatchJob
        {
            CompetitionId = competitionId,
            Type = type,
            PayloadJson = payloadJson,
            Status = status,
            LeaseOwner = leaseOwner,
            LeaseExpiresAt = leaseExpiresAt,
            NextAttemptAt = nextAttemptAt,
            Attempts = attempts,
            LastError = lastError,
        };
        db.DispatchJobs.Add(job);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return job;
    }

    private async Task<DispatchJob> LoadAsync(Guid jobId)
    {
        await using var db = NewDb();
        return await db.DispatchJobs.AsNoTracking().SingleAsync(j => j.Id == jobId, TestContext.Current.CancellationToken);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string because, TimeSpan? timeout = null)
    {
        var limit = timeout ?? PollTimeout;
        using var cts = new CancellationTokenSource(limit);
        while (!await condition())
        {
            if (cts.IsCancellationRequested)
            {
                Assert.Fail($"Timed out after {limit}: {because}");
            }

            await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ForeignLease_ExpiringAfterStartupSweep_IsRecoveredAndProcessedWithoutThePoll()
    {
        var competitionId = await SeedCompetitionAsync();
        var handler = new CountingHandler();
        await using var host = NewHost(handler);
        await ParkWorkerAsync(host, competitionId);

        // Seeded only after the worker parked, so no startup sweep can see it, however slow the
        // host start was: a signalled sweep sees the live lease; only the scheduled wake can
        // recover it later (the poll is 1 h).
        var job = await SeedJobAsync(
            competitionId, DispatchJobStatus.Running, leaseOwner: "dead-worker",
            leaseExpiresAt: DateTimeOffset.UtcNow.AddSeconds(2));
        Signal(host);

        await WaitUntilAsync(
            async () => (await LoadAsync(job.Id)).Status == DispatchJobStatus.Completed,
            "job with an expiring foreign lease recovered and Completed (1 h poll must not be needed)");

        var after = await LoadAsync(job.Id);
        Assert.Equal(1, after.Attempts); // the interrupted attempt counted once
        Assert.Equal(1, handler.Calls[job.Id]);
    }

    [Fact]
    public async Task PendingJob_WithFutureNextAttemptAt_AtStartup_IsProcessedWithoutThePoll()
    {
        var competitionId = await SeedCompetitionAsync();
        var handler = new CountingHandler();
        await using var host = NewHost(handler);
        await ParkWorkerAsync(host, competitionId);

        var job = await SeedJobAsync(
            competitionId, DispatchJobStatus.Pending, nextAttemptAt: DateTimeOffset.UtcNow.AddSeconds(2), attempts: 1);
        Signal(host); // the sweep skips the not-yet-due job and must schedule its own wake

        await WaitUntilAsync(
            async () => (await LoadAsync(job.Id)).Status == DispatchJobStatus.Completed,
            "backed-off Pending job processed shortly after NextAttemptAt (1 h poll must not be needed)");

        Assert.Equal(1, handler.Calls[job.Id]);
    }

    [Fact]
    public async Task FailedSweep_AtScheduledWake_StillProcessesTheDueJobWithoutThePoll()
    {
        var competitionId = await SeedCompetitionAsync();
        var handler = new CountingHandler();
        var fault = new SweepFaultInterceptor();
        await using var host = NewHost(handler, fault);
        await ParkWorkerAsync(host, competitionId);

        var job = await SeedJobAsync(
            competitionId, DispatchJobStatus.Pending, nextAttemptAt: DateTimeOffset.UtcNow.AddSeconds(2), attempts: 1);
        Signal(host); // healthy sweep: skips the not-yet-due job, schedules the wake for ~3 s
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        fault.Arm(); // the sweep at the scheduled wake hits a transient DB fault (claim + schedule)

        await WaitUntilAsync(
            async () => (await LoadAsync(job.Id)).Status == DispatchJobStatus.Completed,
            "due job processed although the sweep at its scheduled wake failed (1 h poll must not be needed)",
            TimeSpan.FromSeconds(30));

        Assert.Equal(1, fault.ClaimFaults);
        Assert.Equal(1, fault.ScheduleFaults);
        Assert.Equal(1, handler.Calls[job.Id]);
    }

    [Fact]
    public async Task Retry_dispatch_wakes_the_worker_without_the_safety_net_poll()
    {
        await using var host = NewHost();
        using var organizer = host.CreateClient(); // starts the host: real handlers, FakeEmailSender
        organizer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtIssuer.IssueToken("organizer-wakeup", null, "ORGANIZER"));
        var sender = (FakeEmailSender)host.Services.GetRequiredService<IEmailSender>();

        var competitionId = await SeedCompetitionAsync();
        var email = $"brewer-{Guid.NewGuid():N}@brew.example";
        Guid participantId;
        await using (var db = NewDb())
        {
            var participant = new Participant { CompetitionId = competitionId, Name = "Brewer", Email = email };
            db.Participants.Add(participant);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            participantId = participant.Id;
        }

        // Sentinel: once it finishes the worker has completed its startup sweep and is parked on
        // the wake-up channel, so only the retry's own signal can wake it (the poll is 1 h).
        Guid sentinelParticipantId;
        await using (var db = NewDb())
        {
            var other = new Participant { CompetitionId = competitionId, Name = "Other", Email = $"other-{Guid.NewGuid():N}@brew.example" };
            db.Participants.Add(other);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            sentinelParticipantId = other.Id;
        }

        var sentinel = await SeedJobAsync(
            competitionId, DispatchJobStatus.Pending, type: DispatchJobType.SendResultEmail,
            payloadJson: JsonSerializer.Serialize(new SendResultEmailPayload(sentinelParticipantId)));

        Assert.True(host.Services.GetRequiredService<Channel<Guid>>().Writer.TryWrite(sentinel.Id));
        await WaitUntilAsync(
            async () => (await LoadAsync(sentinel.Id)).Status != DispatchJobStatus.Pending,
            "sentinel picked up by the worker");
        await WaitUntilAsync(
            async () => (await LoadAsync(sentinel.Id)).Status != DispatchJobStatus.Running,
            "sentinel finished");
        // Let the sweep loop run its final empty claim and park (no observable signal for this).
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        var failed = await SeedJobAsync(
            competitionId, DispatchJobStatus.Failed, type: DispatchJobType.SendResultEmail,
            payloadJson: JsonSerializer.Serialize(new SendResultEmailPayload(participantId)),
            attempts: 5, lastError: "simulated SMTP failure");

        var response = await organizer.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/dispatch/retries",
            new { participantIds = new[] { participantId } },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await WaitUntilAsync(
            async () => (await LoadAsync(failed.Id)).Status == DispatchJobStatus.Completed,
            "retried job reprocessed by a worker woken by RetryDispatch");
        Assert.Contains(sender.Sent, s => s.ToEmail == email);
    }
}
