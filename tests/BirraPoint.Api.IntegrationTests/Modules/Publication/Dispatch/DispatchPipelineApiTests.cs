using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.DispatchScoreSheet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Publication.Dispatch;

/// <summary>
/// HTTP-level contract tests for the results dispatch pipeline (contracts/rest-api.md
/// §Results &amp; dispatch, FR-036/FR-040/FR-041) — this drives the real GeneratePdfs -> BundleZip ->
/// SendResultEmail pipeline end-to-end through the live DispatchWorker hosted service (same
/// approach as the Judges feature tests' async invitation checks), plus backfills HTTP-level coverage for the
/// pre-existing Finalize "tables-still-open" gate (Modules/Organization/Features/Competitions/Features/ChangeCompetitionState/ChangeCompetitionStateCommand.cs), which
/// previously had zero unit/integration coverage.
/// </summary>
public sealed class DispatchPipelineApiTests(ApiFactory factory) : DispatchApiTestBase(factory), IClassFixture<ApiFactory>
{
    // Crash-resume (an orphaned Pending job with no wake-up signal) is only noticed on the next
    // configured safety-net poll sweep, so that test needs a longer allowance than every other poll here. Manual
    // retry wakes the worker directly and uses the normal PollTimeout.
    private static readonly TimeSpan SafetyNetPollTimeout = TimeSpan.FromSeconds(35);

    /// <summary>Seeds one table with a single judge, closes it (mirrors
    /// MonitoringApiTestBase's pattern of seeding evaluations directly and closing via the judge who
    /// submitted them), then transitions the competition through Active/InEvaluation. The caller
    /// still owns calling FinalizeAsync so tests can assert on the Finalize response itself.</summary>
    private async Task<(Guid CompetitionId, string CompetitionName, List<(Guid ParticipantId, string Email, Guid EntryId, string BlindCode)> Entries)>
        SeedClosedTableCompetitionAsync(HttpClient organizer, string namePrefix, int entryCount)
    {
        var (competitionId, competitionName) = await CreateCompetitionWithNameAsync(organizer, namePrefix);
        var tableId = await SeedTableAsync(competitionId, "Table A");

        var judgeSub = $"judge-{Guid.NewGuid():N}";
        var judgeId = await SeedJudgeAsync(competitionId, $"{judgeSub}@brew.example", judgeSub);
        await SeedTableJudgeAsync(tableId, judgeId);

        var entries = new List<(Guid ParticipantId, string Email, Guid EntryId, string BlindCode)>();
        for (var i = 0; i < entryCount; i++)
        {
            var email = $"brewer-{Guid.NewGuid():N}@brew.example";
            var participantId = await SeedParticipantAsync(competitionId, $"Brewer {i}", email);
            var blindCode = NewBlindCode();
            var entryId = await SeedBeerEntryAsync(competitionId, participantId, $"Beer {i}", blindCode);
            await SeedTableSampleAsync(tableId, entryId);
            await SeedEvaluationAsync(tableId, judgeId, entryId);
            entries.Add((participantId, email, entryId, blindCode));
        }

        using var judgeClient = JudgeClient(judgeSub);
        var closeResponse = await judgeClient.PostAsync(Urls.Rel($"/api/v1/me/tables/{tableId}/close"), content: null);
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);

        var toInEvaluation = await TransitionToActiveAndInEvaluationAsync(organizer, competitionId);
        Assert.Equal(HttpStatusCode.OK, toInEvaluation.StatusCode);

        return (competitionId, competitionName, entries);
    }

    // ---- ChangeState backfill: Finalize's tables-still-open gate (previously untested) ---------

    [Fact]
    public async Task FinalizeWithAnOpenTableReturns409TablesStillOpenWithTheBlockingTableId()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer, "OpenTable");
        var openTableId = await SeedTableAsync(competitionId, "Still Open");

        await TransitionToActiveAndInEvaluationAsync(organizer, competitionId);

        var response = await FinalizeAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:tables-still-open", document.RootElement.GetProperty("type").GetString());
        var openTableIds = document.RootElement.GetProperty("openTableIds").EnumerateArray().Select(e => e.GetGuid()).ToList();
        Assert.Contains(openTableId, openTableIds);
    }

    [Fact]
    public async Task FinalizeAFullyClosedCompetitionSucceedsAndEnqueuesAGeneratePdfsJob()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (competitionId, _, _) = await SeedClosedTableCompetitionAsync(organizer, "Closed", entryCount: 1);

        var response = await FinalizeAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasGeneratePdfsJob = await db.DispatchJobs
            .AnyAsync(j => j.CompetitionId == competitionId && j.Type == DispatchJobType.GeneratePdfs, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(hasGeneratePdfsJob);
    }

    // ---- Concurrency: the state change must be serialized, not just sequentially rejected --------

    private const int ConcurrentStateRequests = 20;

    private static async Task<HttpResponseMessage[]> PostStateConcurrentlyAsync(HttpClient organizer, Guid competitionId, string target)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, ConcurrentStateRequests).Select(async _ =>
        {
            await start.Task;
            return await organizer.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target });
        }).ToList();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }

    private async Task<int> CountStateChangeAuditRowsAsync(Guid competitionId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.AsNoTracking().CountAsync(
            a => a.Action == "CompetitionStateChanged" && a.EntityId == competitionId.ToString(),
            TestContext.Current.CancellationToken);
    }

    private static async Task AssertExactlyOneWinnerAsync(HttpResponseMessage[] responses)
    {
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var losers = responses.Where(r => r.StatusCode != HttpStatusCode.OK).ToList();
        Assert.Equal(ConcurrentStateRequests - 1, losers.Count);
        foreach (var loser in losers)
        {
            Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
            using var document = JsonDocument.Parse(await loser.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
        }
    }

    [Fact]
    public async Task ConcurrentFinalizeRequestsYieldOneWinnerOneGeneratePdfsJobAndOneAuditRow()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (competitionId, _, _) = await SeedClosedTableCompetitionAsync(organizer, "FinalizeRace", entryCount: 1);

        var responses = await PostStateConcurrentlyAsync(organizer, competitionId, "Finalized");

        await AssertExactlyOneWinnerAsync(responses);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobCount = await db.DispatchJobs.AsNoTracking().CountAsync(
            j => j.CompetitionId == competitionId && j.Type == DispatchJobType.GeneratePdfs, TestContext.Current.CancellationToken);
        Assert.Equal(1, jobCount);

        // Active + InEvaluation (setup) + exactly one Finalized.
        Assert.Equal(3, await CountStateChangeAuditRowsAsync(competitionId));
    }

    [Fact]
    public async Task ConcurrentActiveToInEvaluationRequestsYieldOneWinnerAndOneAuditRow()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer, "InEvaluationRace");
        var toActive = await organizer.PostAsJsonAsync(
            $"/api/v1/competitions/{competitionId}/state", new { target = "Active" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, toActive.StatusCode);

        var responses = await PostStateConcurrentlyAsync(organizer, competitionId, "InEvaluation");

        await AssertExactlyOneWinnerAsync(responses);
        // Active (setup) + exactly one InEvaluation.
        Assert.Equal(2, await CountStateChangeAuditRowsAsync(competitionId));
    }

    // ---- Full pipeline: GeneratePdfs -> BundleZip -> SendResultEmail ----------------------------

    [Fact]
    public async Task FullPipelineGeneratesAZipAtTheFR040PathAndEmailsEveryParticipantWithAttachments()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (competitionId, competitionName, entries) =
            await SeedClosedTableCompetitionAsync(organizer, "E2E", entryCount: 2);

        var finalizeResponse = await FinalizeAsync(organizer, competitionId);
        Assert.Equal(HttpStatusCode.OK, finalizeResponse.StatusCode);

        var zipBytes = await PollForArchiveReadyAsync(organizer, competitionId);

        using var zipStream = new MemoryStream(zipBytes);
        using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        foreach (var entry in entries)
        {
            var expectedPath = DispatchPaths.ZipEntryPath(competitionName, entry.ParticipantId, StyleCodeApa, entry.BlindCode);
            var zipEntry = zipArchive.GetEntry(expectedPath);
            Assert.NotNull(zipEntry);

            await using var entryStream = await zipEntry.OpenAsync(TestContext.Current.CancellationToken);
            using var pdfBytes = new MemoryStream();
            await entryStream.CopyToAsync(pdfBytes, TestContext.Current.CancellationToken);
            // %PDF is the standard PDF magic header — enough to confirm this is a real PDF, not
            // parsing its content (not required by this test).
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdfBytes.ToArray(), 0, 4), StringComparison.Ordinal);
        }

        var dispatchRows = await PollForAllDispatchRowsCompletedAsync(organizer, competitionId, entries.Count);
        foreach (var entry in entries)
        {
            var row = dispatchRows.Single(r => r.GetProperty("participantId").GetGuid() == entry.ParticipantId);
            Assert.Equal(entry.Email, row.GetProperty("email").GetString());
            Assert.Equal(0, row.GetProperty("attempts").GetInt32());
        }

        foreach (var entry in entries)
        {
            var sent = Assert.Single(FakeEmailSender.Sent, s => s.ToEmail == entry.Email);
            Assert.NotEmpty(sent.Attachments);
            Assert.All(sent.Attachments, a => Assert.Equal("application/pdf", a.ContentType));
        }
    }

    // ---- Regression: fan-out enqueues must be idempotent under DispatchWorker's at-least-once
    // handler contract ------------------------------------

    [Fact]
    public async Task ResettingACompletedBundleZipJobBackToPendingDoesNotDoubleEnqueueSendResultEmailJobs()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (competitionId, _, entries) =
            await SeedClosedTableCompetitionAsync(organizer, "Idempotent", entryCount: 2);

        var finalizeResponse = await FinalizeAsync(organizer, competitionId);
        Assert.Equal(HttpStatusCode.OK, finalizeResponse.StatusCode);

        await PollForArchiveReadyAsync(organizer, competitionId);
        await PollForAllDispatchRowsCompletedAsync(organizer, competitionId, entries.Count);

        // Simulates DispatchWorker's own crash-resume path (ResumeInterruptedJobsAsync / a failed
        // final "mark Completed" save both reset a job back to Pending) — the BundleZip handler's
        // fan-out loop already committed durably on the first, successful attempt, so a re-run
        // must recognize that and skip re-enqueueing rather than doubling every participant's
        // SendResultEmail job. Reset directly via DB (no wake-up signal), so the worker only
        // notices on its periodic safety-net sweep, same as a real crash-resume would.
        var updatedAtBeforeReset = await ResetBundleZipJobToPendingAsync(competitionId);

        // Wait for the worker to actually pick the reset job back up and reprocess it to
        // completion — polling on the job's own UpdatedAt/Status rather than the dispatch-status
        // count, since the pre-existing 2 rows are already Completed and would otherwise satisfy a
        // count-based poll instantly, before the (buggy, pre-fix) re-enqueue ever had a chance to
        // run.
        await PollForBundleZipReprocessedAsync(competitionId, updatedAtBeforeReset);

        // Give any newly (incorrectly, if the guard were missing) re-enqueued SendResultEmail jobs
        // time to actually be sent before counting — they wake the worker immediately on enqueue.
        await PollForAllDispatchRowsCompletedAsync(
            organizer, competitionId, expectedCount: entries.Count, timeout: PollTimeout);

        var sendResultEmailJobCount = await CountDispatchJobsAsync(competitionId, DispatchJobType.SendResultEmail);
        Assert.Equal(entries.Count, sendResultEmailJobCount);

        foreach (var entry in entries)
        {
            Assert.Single(FakeEmailSender.Sent, s => s.ToEmail == entry.Email);
        }
    }

    /// <summary>Resets the competition's BundleZip job back to Pending and returns the
    /// post-reset UpdatedAt, so a caller can distinguish "reprocessed since the reset" from the
    /// job's pre-existing Completed state (which would otherwise satisfy a naive status check
    /// immediately, before the worker ever picks the reset job back up).</summary>
    private async Task<DateTimeOffset> ResetBundleZipJobToPendingAsync(Guid competitionId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.DispatchJobs
            .SingleAsync(j => j.CompetitionId == competitionId && j.Type == DispatchJobType.BundleZip);
        job.Status = DispatchJobStatus.Pending;
        job.NextAttemptAt = null;
        await db.SaveChangesAsync();
        return job.UpdatedAt;
    }

    private async Task PollForBundleZipReprocessedAsync(Guid competitionId, DateTimeOffset resetAt)
    {
        var deadline = DateTime.UtcNow + SafetyNetPollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.DispatchJobs
                .AsNoTracking()
                .SingleAsync(j => j.CompetitionId == competitionId && j.Type == DispatchJobType.BundleZip);

            if (job.Status == DispatchJobStatus.Completed && job.UpdatedAt > resetAt)
            {
                return;
            }

            await Task.Delay(PollInterval);
        }

        Assert.Fail("Timed out waiting for the reset BundleZip job to be reprocessed.");
    }

    private async Task<int> CountDispatchJobsAsync(Guid competitionId, DispatchJobType type)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.DispatchJobs.CountAsync(j => j.CompetitionId == competitionId && j.Type == type);
    }


    // ---- Ownership scoping (404, never leaking existence) ---------------------------------------

    [Fact]
    public async Task ResultsDispatchEndpointsForACompetitionOwnedByADifferentOrganizerReturn404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner, "Cross");

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, (await GetResultsArchiveAsync(other, competitionId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetDispatchStatusAsync(other, competitionId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RetryDispatchAsync(other, competitionId, Guid.NewGuid())).StatusCode);
    }
}
