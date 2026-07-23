using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Email;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Dispatch;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Dispatch;

/// <summary>
/// T072-T076: HTTP-level contract tests for the results dispatch pipeline (contracts/rest-api.md
/// §Results &amp; dispatch, FR-036/FR-040/FR-041) — this drives the real GeneratePdfs -> BundleZip ->
/// SendResultEmail pipeline end-to-end through the live DispatchWorker hosted service (same
/// approach as JudgesApiTests' async invitation tests), plus backfills HTTP-level coverage for the
/// pre-existing Finalize "tables-still-open" gate (Features/Competitions/ChangeState.cs), which
/// previously had zero unit/integration coverage.
/// </summary>
public sealed class DispatchApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string StyleCodeApa = "21A";
    private const string LongComment = "This comment is long enough to satisfy the minimum length rule.";

    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    // DispatchWorker's manual-retry path deliberately has no proactive wake-up signal (contracts:
    // "no latency SLA on manual retry pickup") — a retried job is only noticed on the next 30s
    // safety-net sweep, so that one test needs a longer allowance than every other poll here.
    private static readonly TimeSpan SafetyNetPollTimeout = TimeSpan.FromSeconds(35);

    private HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    private HttpClient JudgeClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, $"{sub}@brew.example", "JUDGE"));
        return client;
    }

    private FakeEmailSender FakeEmailSender => (FakeEmailSender)factory.Services.GetRequiredService<IEmailSender>();

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Dispatch") =>
        (await CreateCompetitionWithNameAsync(client, namePrefix)).Id;

    private static async Task<(Guid Id, string Name)> CreateCompetitionWithNameAsync(HttpClient client, string namePrefix)
    {
        var name = $"{namePrefix} {Guid.NewGuid():N}";
        var response = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            name,
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        });
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (created.GetProperty("id").GetGuid(), name);
    }

    private static string NewBlindCode() => $"B{Guid.NewGuid():N}"[..8];

    private static async Task<HttpResponseMessage> TransitionToActiveAndInEvaluationAsync(HttpClient organizer, Guid competitionId)
    {
        await organizer.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "Active" });
        return await organizer.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "InEvaluation" });
    }

    private static Task<HttpResponseMessage> FinalizeAsync(HttpClient organizer, Guid competitionId) =>
        organizer.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/state", new { target = "Finalized" });

    private async Task<Guid> SeedParticipantAsync(Guid competitionId, string name, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var participant = new Participant { CompetitionId = competitionId, Name = name, Email = email };
        db.Participants.Add(participant);
        await db.SaveChangesAsync();
        return participant.Id;
    }

    private async Task<Guid> SeedBeerEntryAsync(
        Guid competitionId, Guid participantId, string beerName, string blindCode, string styleCode = StyleCodeApa)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = new BeerEntry
        {
            CompetitionId = competitionId,
            ParticipantId = participantId,
            BeerName = beerName,
            StyleCode = styleCode,
            BlindCode = blindCode,
        };
        db.BeerEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<Guid> SeedJudgeAsync(Guid competitionId, string email, string keycloakUserId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judge = new Judge
        {
            CompetitionId = competitionId,
            Email = email,
            DisplayName = email.Split('@')[0],
            KeycloakUserId = keycloakUserId,
        };
        db.Judges.Add(judge);
        await db.SaveChangesAsync();
        return judge.Id;
    }

    private async Task<Guid> SeedTableAsync(Guid competitionId, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var table = new TastingTable { CompetitionId = competitionId, Name = name };
        db.TastingTables.Add(table);
        await db.SaveChangesAsync();
        return table.Id;
    }

    private async Task SeedTableJudgeAsync(Guid tableId, Guid judgeId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableJudges.Add(new TableJudge { TastingTableId = tableId, JudgeId = judgeId });
        await db.SaveChangesAsync();
    }

    private async Task SeedTableSampleAsync(Guid tableId, Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.TableSamples.Add(new TableSample { TastingTableId = tableId, BeerEntryId = beerEntryId });
        await db.SaveChangesAsync();
    }

    private async Task SeedEvaluationAsync(Guid tableId, Guid judgeId, Guid beerEntryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Evaluations.Add(new Evaluation
        {
            TastingTableId = tableId,
            JudgeId = judgeId,
            BeerEntryId = beerEntryId,
            AromaScore = 10,
            AppearanceScore = 2,
            FlavorScore = 15,
            MouthfeelScore = 4,
            OverallScore = 8,
            AromaComment = LongComment,
            AppearanceComment = LongComment,
            FlavorComment = LongComment,
            MouthfeelComment = LongComment,
            OverallComment = LongComment,
            Status = EvaluationStatus.Confirmed,
            SubmittedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedDispatchJobAsync(
        Guid competitionId, DispatchJobType type, DispatchJobStatus status, object payload,
        int attempts = 0, string? lastError = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = new DispatchJob
        {
            CompetitionId = competitionId,
            Type = type,
            PayloadJson = JsonSerializer.Serialize(payload),
            Status = status,
            Attempts = attempts,
            LastError = lastError,
        };
        db.DispatchJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    /// <summary>Seeds one table with a single judge, closes it (mirrors
    /// MonitoringApiTests' pattern of seeding evaluations directly and closing via the judge who
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
        var closeResponse = await judgeClient.PostAsync($"/api/v1/me/tables/{tableId}/close", content: null);
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);

        var toInEvaluation = await TransitionToActiveAndInEvaluationAsync(organizer, competitionId);
        Assert.Equal(HttpStatusCode.OK, toInEvaluation.StatusCode);

        return (competitionId, competitionName, entries);
    }

    private static Task<HttpResponseMessage> GetResultsArchiveAsync(HttpClient client, Guid competitionId) =>
        client.GetAsync($"/api/v1/competitions/{competitionId}/results/archive");

    private static Task<HttpResponseMessage> GetDispatchStatusAsync(HttpClient client, Guid competitionId) =>
        client.GetAsync($"/api/v1/competitions/{competitionId}/dispatch");

    private static Task<HttpResponseMessage> RetryDispatchAsync(HttpClient client, Guid competitionId, params Guid[] participantIds) =>
        client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/dispatch/retries", new { participantIds });

    private static async Task<byte[]> PollForArchiveReadyAsync(HttpClient organizer, Guid competitionId)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var response = await GetResultsArchiveAsync(organizer, competitionId);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return await response.Content.ReadAsByteArrayAsync();
            }

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            await Task.Delay(PollInterval);
        }

        Assert.Fail("Timed out waiting for the results archive to become ready.");
        throw new UnreachableException();
    }

    private static async Task<List<JsonElement>> PollForAllDispatchRowsCompletedAsync(
        HttpClient organizer, Guid competitionId, int expectedCount, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? PollTimeout);
        while (DateTime.UtcNow < deadline)
        {
            var response = await GetDispatchStatusAsync(organizer, competitionId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var rows = document.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();

            if (rows.Count == expectedCount && rows.All(r => r.GetProperty("status").GetString() == "Completed"))
            {
                return rows;
            }

            await Task.Delay(PollInterval);
        }

        Assert.Fail("Timed out waiting for every dispatch job to reach Completed.");
        throw new UnreachableException();
    }

    // ---- ChangeState backfill: Finalize's tables-still-open gate (previously untested) ---------

    [Fact]
    public async Task Finalize_with_an_open_table_returns_409_tables_still_open_with_the_blocking_table_id()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer, "OpenTable");
        var openTableId = await SeedTableAsync(competitionId, "Still Open");

        await TransitionToActiveAndInEvaluationAsync(organizer, competitionId);

        var response = await FinalizeAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:tables-still-open", document.RootElement.GetProperty("type").GetString());
        var openTableIds = document.RootElement.GetProperty("openTableIds").EnumerateArray().Select(e => e.GetGuid()).ToList();
        Assert.Contains(openTableId, openTableIds);
    }

    [Fact]
    public async Task Finalize_a_fully_closed_competition_succeeds_and_enqueues_a_GeneratePdfs_job()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var (competitionId, _, _) = await SeedClosedTableCompetitionAsync(organizer, "Closed", entryCount: 1);

        var response = await FinalizeAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasGeneratePdfsJob = await db.DispatchJobs
            .AnyAsync(j => j.CompetitionId == competitionId && j.Type == DispatchJobType.GeneratePdfs);
        Assert.True(hasGeneratePdfsJob);
    }

    // ---- Full pipeline: GeneratePdfs -> BundleZip -> SendResultEmail ----------------------------

    [Fact]
    public async Task Full_pipeline_generates_a_zip_at_the_FR040_path_and_emails_every_participant_with_attachments()
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

            using var entryStream = zipEntry!.Open();
            using var pdfBytes = new MemoryStream();
            await entryStream.CopyToAsync(pdfBytes);
            // %PDF is the standard PDF magic header — enough to confirm this is a real PDF, not
            // parsing its content (not required by this test).
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdfBytes.ToArray(), 0, 4));
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
    // handler contract (senior-code-reviewer finding on PR #25) ------------------------------------

    [Fact]
    public async Task Resetting_a_completed_BundleZip_job_back_to_Pending_does_not_double_enqueue_SendResultEmail_jobs()
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
        await using var scope = factory.Services.CreateAsyncScope();
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
            await using var scope = factory.Services.CreateAsyncScope();
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
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.DispatchJobs.CountAsync(j => j.CompetitionId == competitionId && j.Type == type);
    }

    [Fact]
    public async Task Get_results_archive_before_the_pipeline_completes_returns_202_with_status()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer, "Pending");

        // A BundleZip job stuck Running is never touched by DispatchWorker's periodic sweep
        // (only Pending jobs are dispatched, and only a startup resume-sweep touches Running ones)
        // — this keeps the "not ready yet" state deterministic for the lifetime of this test.
        await SeedDispatchJobAsync(competitionId, DispatchJobType.BundleZip, DispatchJobStatus.Running, new { });

        var response = await GetResultsArchiveAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Running", document.RootElement.GetProperty("status").GetString());
    }

    // ---- Retry ------------------------------------------------------------------------------

    [Fact]
    public async Task Retry_dispatch_resets_a_failed_job_and_it_gets_reprocessed_by_the_safety_net_poll()
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
        var statusResponse = await GetDispatchStatusAsync(organizer, competitionId);
        using var document = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        var row = document.RootElement.EnumerateArray().Single();
        Assert.True(row.GetProperty("status").GetString() is "Pending" or "Completed");
        Assert.Null(row.GetProperty("lastError").GetString());

        await PollForAllDispatchRowsCompletedAsync(organizer, competitionId, expectedCount: 1, SafetyNetPollTimeout);
        Assert.Contains(FakeEmailSender.Sent, s => s.ToEmail == email);
    }

    // ---- Ownership scoping (404, never leaking existence) ---------------------------------------

    [Fact]
    public async Task Results_dispatch_endpoints_for_a_competition_owned_by_a_different_organizer_return_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner, "Cross");

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, (await GetResultsArchiveAsync(other, competitionId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetDispatchStatusAsync(other, competitionId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RetryDispatchAsync(other, competitionId, Guid.NewGuid())).StatusCode);
    }
}
