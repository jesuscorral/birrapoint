using System.Net;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Dispatch.CommonComponents;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Dispatch.Features.RetryDispatch;

public sealed class RetryDispatchApiTests(ApiFactory factory) : DispatchApiTestBase(factory), IClassFixture<ApiFactory>
{
    private static readonly string[] InFlightOrCompletedStatuses = ["Pending", "Running", "Completed"];

    // The wake-up guarantee (no safety-net poll needed) is covered deterministically by
    // DispatchWakeUpTests.Retry_dispatch_wakes_the_worker_without_the_safety_net_poll.
    [Fact]
    public async Task RetryDispatchResetsAFailedJobAndItGetsReprocessed()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer, "Retry");
        var email = $"brewer-{Guid.NewGuid():N}@brew.example";
        var participantId = await SeedParticipantAsync(competitionId, "Brewer", email);

        await SeedDispatchJobAsync(
            competitionId, DispatchJobType.SendResultEmail, DispatchJobStatus.Failed,
            new SendResultEmailPayload(participantId), attempts: 5, lastError: "simulated SMTP failure");

        var retryResponse = await RetryDispatchAsync(organizer, competitionId, participantId);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);

        // Reset itself is synchronous within the retry request — no polling needed for this part.
        // The job is no longer Failed and its error is cleared; the DispatchWorker (woken by the retry or the poll)
        // may already have picked it up, so Running (mid-handler) is as valid as Pending or
        // Completed here (T138: asserting only Pending/Completed made this test flaky).
        var statusResponse = await GetDispatchStatusAsync(organizer, competitionId);
        using var document = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var row = document.RootElement.EnumerateArray().Single();
        Assert.Contains(row.GetProperty("status").GetString(), InFlightOrCompletedStatuses);
        Assert.Null(row.GetProperty("lastError").GetString());

        await PollForAllDispatchRowsCompletedAsync(organizer, competitionId, expectedCount: 1, PollTimeout);
        Assert.Contains(FakeEmailSender.Sent, s => s.ToEmail == email);
    }

    [Fact]
    public async Task RetryDispatchLeavesCompletedAndRunningJobsUntouched()
    {
        // FR-041 retries failures only: resetting a Completed job would resend the email, and
        // resetting a Running job (live lease, possibly another revision's worker, T129) would run
        // it twice.
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer, "RetryUntouched");
        var completedParticipant = await SeedParticipantAsync(
            competitionId, "Done", $"done-{Guid.NewGuid():N}@brew.example");
        var runningParticipant = await SeedParticipantAsync(
            competitionId, "Busy", $"busy-{Guid.NewGuid():N}@brew.example");

        var completedJobId = await SeedDispatchJobAsync(
            competitionId, DispatchJobType.SendResultEmail, DispatchJobStatus.Completed,
            new SendResultEmailPayload(completedParticipant), attempts: 1);
        var runningJobId = await SeedDispatchJobAsync(
            competitionId, DispatchJobType.SendResultEmail, DispatchJobStatus.Running,
            new SendResultEmailPayload(runningParticipant), attempts: 1);
        var leaseExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.DispatchJobs.Where(j => j.Id == runningJobId).ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.LeaseOwner, "other-revision-worker")
                    .SetProperty(j => j.LeaseExpiresAt, leaseExpiresAt),
                TestContext.Current.CancellationToken);
        }

        var retryResponse = await RetryDispatchAsync(organizer, competitionId, completedParticipant, runningParticipant);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);

        await using var verifyScope = Factory.Services.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var completed = await verify.DispatchJobs.AsNoTracking()
            .SingleAsync(j => j.Id == completedJobId, TestContext.Current.CancellationToken);
        Assert.Equal(DispatchJobStatus.Completed, completed.Status);
        Assert.Equal(1, completed.Attempts);

        var running = await verify.DispatchJobs.AsNoTracking()
            .SingleAsync(j => j.Id == runningJobId, TestContext.Current.CancellationToken);
        Assert.Equal(DispatchJobStatus.Running, running.Status);
        Assert.Equal(1, running.Attempts);
        Assert.Equal("other-revision-worker", running.LeaseOwner);
    }
}
