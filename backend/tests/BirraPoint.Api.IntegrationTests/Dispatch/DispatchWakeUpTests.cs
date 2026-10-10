using System.Collections.Concurrent;
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

    private AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.ConnectionString).Options);

    private WebApplicationFactory<Program> NewHost(IDispatchJobHandler? handler = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Dispatch:LeaseDuration"] = "00:00:05",
                    ["Dispatch:SafetyNetPollInterval"] = "01:00:00",
                }));
            if (handler is not null)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IDispatchJobHandler>();
                    services.AddSingleton(handler);
                });
            }
        });

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

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string because)
    {
        using var cts = new CancellationTokenSource(PollTimeout);
        while (!await condition())
        {
            if (cts.IsCancellationRequested)
            {
                Assert.Fail($"Timed out after {PollTimeout}: {because}");
            }

            await Task.Delay(PollInterval, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task ForeignLease_ExpiringAfterStartupSweep_IsRecoveredAndProcessedWithoutThePoll()
    {
        var competitionId = await SeedCompetitionAsync();
        var job = await SeedJobAsync(
            competitionId, DispatchJobStatus.Running, leaseOwner: "dead-worker",
            leaseExpiresAt: DateTimeOffset.UtcNow.AddSeconds(2));
        var handler = new CountingHandler();

        await using var host = NewHost(handler);
        _ = host.Services; // startup sweep sees a live lease and skips the job

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
        var job = await SeedJobAsync(
            competitionId, DispatchJobStatus.Pending, nextAttemptAt: DateTimeOffset.UtcNow.AddSeconds(2), attempts: 1);
        var handler = new CountingHandler();

        await using var host = NewHost(handler);
        _ = host.Services; // startup sweep skips the not-yet-due job

        await WaitUntilAsync(
            async () => (await LoadAsync(job.Id)).Status == DispatchJobStatus.Completed,
            "backed-off Pending job processed shortly after NextAttemptAt (1 h poll must not be needed)");

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
