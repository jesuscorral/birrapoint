using System.Threading.Channels;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BirraPoint.Api.Common.Jobs;

/// <summary>
/// Hosted worker for the <see cref="DispatchJob"/> queue (T016/R-06, T129). Wakes on
/// <paramref name="wakeUpChannel"/> writes from <see cref="DispatchJobQueue"/>, with a periodic
/// safety-net poll so a missed signal or a backoff-delayed retry is never lost. Every job is
/// resolved through a fresh DI scope (AppDbContext is scoped; this worker is a singleton).
///
/// Safe under overlapping API revisions (ACA rollouts run the old and new revision side by side):
/// a job is claimed atomically (<c>FOR UPDATE SKIP LOCKED</c> in one transaction that also marks
/// it Running with <see cref="DispatchJob.LeaseOwner"/>/<see cref="DispatchJob.LeaseExpiresAt"/>),
/// the lease is renewed while the handler runs, and the outcome is written only
/// <c>WHERE LeaseOwner = me</c>. Running jobs whose lease expired (crashed worker) are recovered
/// every cycle through the normal retry path; live leases are never touched.
/// DispatchConcurrencyTests (Testcontainers) covers claim, lease and recovery.
///
/// Every cycle runs inside <see cref="RunGuardedAsync"/>: the default
/// <c>BackgroundServiceExceptionBehavior</c> is <c>StopHost</c>, so an unguarded transient DB
/// error here would take down the entire API — the opposite of R-06's "survive restarts" premise.
/// </summary>
public sealed class DispatchWorker(
    IServiceScopeFactory scopeFactory,
    Channel<Guid> wakeUpChannel,
    IEventPublisher eventPublisher,
    IOptions<DispatchOptions> options,
    ILogger<DispatchWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);
    private const int LastErrorMaxLength = 2000; // matches DispatchJobConfiguration.LastError
    private const int RecoveryBatchSize = 50;

    /// <summary>Unique per process, so two replicas (or revisions) never share a lease identity.</summary>
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    private TimeSpan LeaseDuration => options.Value.LeaseDuration;
    private TimeSpan SafetyNetPollInterval => options.Value.SafetyNetPollInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunGuardedAsync(RecoverExpiredLeasesAsync, stoppingToken);
        await RunGuardedAsync(ProcessPendingJobsAsync, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await WaitForWorkAsync(stoppingToken);

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await RunGuardedAsync(RecoverExpiredLeasesAsync, stoppingToken);
            await RunGuardedAsync(ProcessPendingJobsAsync, stoppingToken);
        }
    }

    /// <summary>A transient fault in a cycle must never fault <see cref="ExecuteAsync"/> — that
    /// would stop the whole host under the default exception behavior.</summary>
    private async Task RunGuardedAsync(Func<CancellationToken, Task> cycle, CancellationToken stoppingToken)
    {
        try
        {
            await cycle(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down — nothing to recover.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "DispatchWorker cycle failed; will retry after the next wake-up.");
            try
            {
                await Task.Delay(ErrorBackoff, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    /// <summary>A Running job whose lease expired belongs to a crashed worker; treat it exactly like
    /// a failed attempt so it goes through the same retry/backoff decision. A job with no lease was
    /// claimed by a pre-lease (pre-T129) revision, which may still be running it during the first
    /// rollout: it counts as orphaned only once it has not been touched for a full lease duration.
    /// Claimed with SKIP LOCKED so concurrent recoverers never double-count an attempt.</summary>
    private async Task RecoverExpiredLeasesAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var now = DateTimeOffset.UtcNow;
            var staleBefore = now - LeaseDuration;
            var running = nameof(DispatchJobStatus.Running);
            await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
            var expired = await db.DispatchJobs
                .FromSql($"""
                    SELECT * FROM "DispatchJobs"
                    WHERE "Status" = {running}
                      AND ("LeaseExpiresAt" < {now}
                           OR ("LeaseExpiresAt" IS NULL AND "UpdatedAt" < {staleBefore}))
                    ORDER BY "CreatedAt"
                    LIMIT {RecoveryBatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(stoppingToken);

            if (expired.Count == 0)
            {
                return;
            }

            var outcomes = expired
                .Select(job => (Job: job, Outcome: ApplyFailure(job, "Interrupted: lease expired while Running.")))
                .ToList();
            await db.SaveChangesAsync(stoppingToken);
            await transaction.CommitAsync(stoppingToken);

            logger.LogWarning("Recovered {Count} DispatchJob(s) with an expired lease.", expired.Count);
            foreach (var (job, outcome) in outcomes)
            {
                await AfterFailureRecordedAsync(job, outcome, exception: null, stoppingToken);
            }

            if (expired.Count < RecoveryBatchSize)
            {
                return;
            }
        }
    }

    private async Task WaitForWorkAsync(CancellationToken stoppingToken)
    {
        var channelWait = wakeUpChannel.Reader.WaitToReadAsync(stoppingToken).AsTask();
        var timerWait = Task.Delay(SafetyNetPollInterval, stoppingToken);
        await Task.WhenAny(channelWait, timerWait);

        // Drain any queued signals now so the channel doesn't accumulate while this cycle runs —
        // the upcoming full sweep already covers whatever they were signaling.
        while (wakeUpChannel.Reader.TryRead(out _))
        {
        }
    }

    private async Task ProcessPendingJobsAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // One job per claim, so a long handler never keeps other jobs' leases ticking.
            var job = await ClaimNextPendingAsync(stoppingToken);
            if (job is null)
            {
                return;
            }

            await ProcessJobAsync(job, stoppingToken);
        }
    }

    /// <summary>Atomically claims the oldest eligible Pending job: the row lock is held only for
    /// this short transaction; afterwards the Running status + lease protect the job.</summary>
    private async Task<DispatchJob?> ClaimNextPendingAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTimeOffset.UtcNow;
        var pending = nameof(DispatchJobStatus.Pending);
        await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
        var job = (await db.DispatchJobs
            .FromSql($"""
                SELECT * FROM "DispatchJobs"
                WHERE "Status" = {pending} AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= {now})
                ORDER BY "CreatedAt"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(stoppingToken)).SingleOrDefault();

        if (job is null)
        {
            return null;
        }

        job.Status = DispatchJobStatus.Running;
        job.LeaseOwner = _workerId;
        job.LeaseExpiresAt = now + LeaseDuration;
        await db.SaveChangesAsync(stoppingToken);
        await transaction.CommitAsync(stoppingToken);
        return job;
    }

    private async Task ProcessJobAsync(DispatchJob job, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IDispatchJobHandler>()
            .ToDictionary(handler => handler.Type);

        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var renewal = RenewLeaseAsync(job.Id, renewalCts.Token);

        try
        {
            if (!handlers.TryGetValue(job.Type, out var handler))
            {
                throw new InvalidOperationException($"No {nameof(IDispatchJobHandler)} registered for {job.Type}.");
            }

            await handler.HandleAsync(job, stoppingToken);
            await StopRenewalAsync(renewalCts, renewal);

            // Non-cancellable inside TryFinishAsync: a handler that already ran to completion must
            // have its outcome durably recorded even if shutdown fires in this instant.
            if (await TryFinishAsync(job, DispatchJobStatus.Completed, attempts: null, lastError: null, nextAttemptAt: null))
            {
                job.Status = DispatchJobStatus.Completed;
                logger.LogInformation("DispatchJob {JobId} ({Type}) completed.", job.Id, job.Type);
                await PublishProgressSafely(job, detail: null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await StopRenewalAsync(renewalCts, renewal);
            var outcome = ApplyFailure(job, ex.Message);
            if (await TryFinishAsync(job, outcome.Status, job.Attempts, job.LastError, outcome.NextAttemptAt))
            {
                await AfterFailureRecordedAsync(job, outcome, ex, stoppingToken);
            }
        }
        finally
        {
            await StopRenewalAsync(renewalCts, renewal);
        }
    }

    private static async Task StopRenewalAsync(CancellationTokenSource renewalCts, Task renewal)
    {
        await renewalCts.CancelAsync();
        await renewal;
    }

    /// <summary>Extends the lease at a third of its duration until cancelled. Failures are logged
    /// and retried on the next tick; a lost lease (0 rows) just stops renewing — the outcome write
    /// is ownership-checked, so the new owner's result is never overwritten.</summary>
    private async Task RenewLeaseAsync(Guid jobId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(LeaseDuration / 3);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var expiresAt = DateTimeOffset.UtcNow + LeaseDuration;
                    var rows = await db.DispatchJobs
                        .Where(j => j.Id == jobId && j.LeaseOwner == _workerId && j.Status == DispatchJobStatus.Running)
                        .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseExpiresAt, expiresAt), cancellationToken);
                    if (rows == 0)
                    {
                        logger.LogWarning("DispatchJob {JobId} lease was lost; stopping renewal.", jobId);
                        return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Failed to renew the lease of DispatchJob {JobId}; will retry.", jobId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Handler finished or host is shutting down.
        }
    }

    /// <summary>Writes the outcome only while this worker still owns the lease, and clears the
    /// lease fields. Returns false when ownership was lost (another worker recovered the job).</summary>
    private async Task<bool> TryFinishAsync(
        DispatchJob job, DispatchJobStatus status, int? attempts, string? lastError, DateTimeOffset? nextAttemptAt)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;

        var query = db.DispatchJobs.Where(j => j.Id == job.Id && j.LeaseOwner == _workerId);
        var rows = attempts is null
            ? await query.ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, status)
                    .SetProperty(j => j.LeaseOwner, (string?)null)
                    .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.UpdatedAt, now),
                CancellationToken.None)
            : await query.ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, status)
                    .SetProperty(j => j.Attempts, attempts.Value)
                    .SetProperty(j => j.LastError, lastError)
                    .SetProperty(j => j.NextAttemptAt, nextAttemptAt)
                    .SetProperty(j => j.LeaseOwner, (string?)null)
                    .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.UpdatedAt, now),
                CancellationToken.None);

        if (rows == 0)
        {
            logger.LogWarning(
                "DispatchJob {JobId} ({Type}) lost its lease before the outcome was recorded; result discarded.",
                job.Id, job.Type);
        }

        return rows > 0;
    }

    private readonly record struct FailureOutcome(DispatchJobStatus Status, DateTimeOffset? NextAttemptAt, TimeSpan? Backoff);

    /// <summary>Applies the retry decision to the in-memory job (Attempts, LastError, Status,
    /// NextAttemptAt, cleared lease); persisting it is the caller's responsibility.</summary>
    private static FailureOutcome ApplyFailure(DispatchJob job, string error)
    {
        job.Attempts++;
        job.LastError = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;
        job.Status = DispatchRetryPolicy.ShouldRetry(job.Attempts) ? DispatchJobStatus.Pending : DispatchJobStatus.Failed;
        job.LeaseOwner = null;
        job.LeaseExpiresAt = null;

        TimeSpan? backoff = null;
        if (job.Status == DispatchJobStatus.Pending)
        {
            backoff = DispatchRetryPolicy.BackoffDelay(job.Attempts);
            job.NextAttemptAt = DateTimeOffset.UtcNow + backoff.Value;
        }

        return new FailureOutcome(job.Status, job.NextAttemptAt, backoff);
    }

    private async Task AfterFailureRecordedAsync(
        DispatchJob job, FailureOutcome outcome, Exception? exception, CancellationToken stoppingToken)
    {
        if (exception is not null)
        {
            logger.LogError(
                exception, "DispatchJob {JobId} ({Type}) failed attempt {Attempts}; next status {Status}.",
                job.Id, job.Type, job.Attempts, job.Status);
        }
        else
        {
            logger.LogWarning(
                "DispatchJob {JobId} ({Type}) recorded as a failed attempt {Attempts} ({Reason}); next status {Status}.",
                job.Id, job.Type, job.Attempts, job.LastError, job.Status);
        }

        await PublishProgressSafely(job, job.LastError);

        if (outcome.Backoff.HasValue)
        {
            ScheduleRetrySignal(job.Id, outcome.Backoff.Value, stoppingToken);
        }
    }

    /// <summary>The DispatchProgress notification is fire-and-forget, not the source of truth
    /// (contracts/signalr-hub.md) — a publish failure must never revert a job's already-persisted
    /// outcome, so this is isolated from the caller's try/catch.</summary>
    private async Task PublishProgressSafely(DispatchJob job, string? detail)
    {
        try
        {
            await eventPublisher.PublishToOrganizersAsync(
                job.CompetitionId, CompetitionEvents.DispatchProgress,
                new DispatchProgressPayload(job.Type, job.Status, detail), CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish DispatchProgress for job {JobId}.", job.Id);
        }
    }

    /// <summary>Best-effort early wake-up for a backed-off retry; the periodic safety-net poll in
    /// <see cref="WaitForWorkAsync"/>, combined with the NextAttemptAt filter in
    /// <see cref="ClaimNextPendingAsync"/>, is what actually enforces the delay — this only
    /// saves the job from waiting out the full safety-net poll interval unnecessarily.</summary>
    private void ScheduleRetrySignal(Guid jobId, TimeSpan delay, CancellationToken stoppingToken) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
                await wakeUpChannel.Writer.WriteAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Host is shutting down — nothing to signal.
            }
        }, stoppingToken);
}
