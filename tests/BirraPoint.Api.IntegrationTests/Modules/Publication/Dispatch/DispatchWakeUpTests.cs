using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Publication.Features.Dispatch.CommonComponents;
using BuildingBlocks.EmailSender;
using BuildingBlocks.Jobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BirraPoint.Api.IntegrationTests.Modules.Publication.Dispatch;

/// <summary>
/// The generic worker scenarios (lease expiry, future retry time, failed sweep) are covered in the
/// Jobs block tests; this one needs the real host: RetryDispatch must signal the worker after
/// resetting Failed jobs, with the periodic safety-net poll set to 1 h so it cannot mask a missing wake-up.
/// </summary>
public sealed class DispatchWakeUpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(factory.ConnectionString).Options);

    private WebApplicationFactory<Program> NewHost(
        IJobHandler? handler = null) =>
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
                    services.RemoveAll<IJobHandler>();
                    services.AddSingleton(handler);
                }
            });
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

    private async Task<Job> SeedJobAsync(
        Guid competitionId, JobStatus status, string? leaseOwner = null,
        DateTimeOffset? leaseExpiresAt = null, DateTimeOffset? nextAttemptAt = null,
        string type = DispatchJobType.SendInvitation, string payloadJson = "{}",
        int attempts = 0, string? lastError = null)
    {
        await using var db = NewDb();
        var job = new Job
        {
            GroupKey = competitionId,
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

    private async Task<Job> LoadAsync(Guid jobId)
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
    public async Task RetryDispatchWakesTheWorkerWithoutTheSafetyNetPoll()
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
            competitionId, JobStatus.Pending, type: DispatchJobType.SendResultEmail,
            payloadJson: JsonSerializer.Serialize(new SendResultEmailPayload(sentinelParticipantId)));

        host.Services.GetRequiredService<IJobWakeUp>().Signal();
        await WaitUntilAsync(
            async () => (await LoadAsync(sentinel.Id)).Status != JobStatus.Pending,
            "sentinel picked up by the worker");
        await WaitUntilAsync(
            async () => (await LoadAsync(sentinel.Id)).Status != JobStatus.Running,
            "sentinel finished");
        // Let the sweep loop run its final empty claim and park (no observable signal for this).
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        var failed = await SeedJobAsync(
            competitionId, JobStatus.Failed, type: DispatchJobType.SendResultEmail,
            payloadJson: JsonSerializer.Serialize(new SendResultEmailPayload(participantId)),
            attempts: 5, lastError: "simulated SMTP failure");

        var response = await organizer.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/dispatch/retries",
            new { participantIds = new[] { participantId } },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await WaitUntilAsync(
            async () => (await LoadAsync(failed.Id)).Status == JobStatus.Completed,
            "retried job reprocessed by a worker woken by RetryDispatch");
        Assert.Contains(sender.Sent, s => s.ToEmail == email);
    }
}
