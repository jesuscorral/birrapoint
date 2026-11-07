using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Jobs;

/// <summary>
/// Hosted worker for the <see cref="Job"/> queue (PostgreSQL only). Wakes on <see cref="IJobWakeUp"/> signals,
/// with a periodic safety-net poll so a missed signal or a backoff-delayed retry is never lost. Every job is
/// resolved through a fresh DI scope (the host DbContext is scoped; this worker is a singleton).
///
/// Safe under overlapping revisions (rolling deployments run the old and new revision side by side): a job is
/// claimed atomically (<c>FOR UPDATE SKIP LOCKED</c> in one transaction that also marks it Running with
/// <see cref="Job.LeaseOwner"/>/<see cref="Job.LeaseExpiresAt"/>), the lease is renewed while the handler
/// runs, and the outcome is written only <c>WHERE LeaseOwner = me</c>. Running jobs whose lease expired
/// (crashed worker) are recovered every cycle through the normal retry path; live leases are never touched.
///
/// Every cycle runs inside <see cref="RunGuardedAsync"/>: the default
/// <c>BackgroundServiceExceptionBehavior</c> is <c>StopHost</c>, so an unguarded transient DB error here
/// would take down the entire host — the opposite of "survive restarts".
/// </summary>
internal sealed class JobWorker<TContext>(
    IServiceScopeFactory scopeFactory,
    JobWakeUp wakeUp,
    IOptions<JobOptions> options,
    ILogger<JobWorker<TContext>> logger)
    : BackgroundService
    where TContext : DbContext
{
    private const int RecoveryBatchSize = 50;

    /// <summary>Unique per process, so two replicas (or revisions) never share a lease identity.</summary>
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    private JobSql? _sql;
    private int _consecutiveFailedSweeps;
    private CancellationTokenSource? _scheduledWake;

    private TimeSpan LeaseDuration => options.Value.LeaseDuration;
    private TimeSpan SafetyNetPollInterval => options.Value.SafetyNetPollInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SweepAsync(stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                await WaitForWorkAsync(stoppingToken).ConfigureAwait(false);

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _scheduledWake?.Dispose();
            _scheduledWake = null;
        }
    }

    /// <summary>Runs recover, process and schedule. A failure in any step leaves the schedule
    /// unreliable (and with a long poll interval nothing else would wake the worker), so a failed
    /// sweep schedules a short retry wake with a growing backoff, capped at the poll interval.</summary>
    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        var ok = await RunGuardedAsync(RecoverExpiredLeasesAsync, stoppingToken).ConfigureAwait(false);
        ok &= await RunGuardedAsync(ProcessPendingJobsAsync, stoppingToken).ConfigureAwait(false);
        ok &= await RunGuardedAsync(ScheduleNextWakeAsync, stoppingToken).ConfigureAwait(false);

        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (ok)
        {
            _consecutiveFailedSweeps = 0;
            return;
        }

        var exponent = Math.Min(_consecutiveFailedSweeps, 20);
        _consecutiveFailedSweeps++;
        var retryDelay = TimeSpan.FromTicks(JobWorkerTiming.ErrorBackoff.Ticks << exponent);
        if (retryDelay > SafetyNetPollInterval || retryDelay <= TimeSpan.Zero)
        {
            retryDelay = SafetyNetPollInterval;
        }

        JobWorkerLog.SweepFailed(logger, _consecutiveFailedSweeps, retryDelay);
        StartWake(retryDelay, stoppingToken);
    }

    /// <summary>A transient fault in a cycle must never fault <see cref="ExecuteAsync"/> — that
    /// would stop the whole host under the default exception behavior.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Background-service resilience boundary: any fault in a cycle is logged and retried; it must never fault ExecuteAsync and stop the host.")]
    private async Task<bool> RunGuardedAsync(Func<CancellationToken, Task> cycle, CancellationToken stoppingToken)
    {
        try
        {
            await cycle(stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down — nothing to recover.
            return true;
        }
        catch (Exception ex)
        {
            // Deliberately broad: a transient fault must never fault ExecuteAsync (see summary above).
            JobWorkerLog.CycleFailed(logger, ex);
            return false;
        }
    }

    /// <summary>A Running job whose lease expired belongs to a crashed worker; treat it exactly like
    /// a failed attempt so it goes through the same retry/backoff decision. A job with no lease was
    /// claimed by a pre-lease revision, which may still be running it during the first
    /// rollout: it counts as orphaned only once it has not been touched for a full lease duration.
    /// Claimed with SKIP LOCKED so concurrent recoverers never double-count an attempt.</summary>
    private async Task RecoverExpiredLeasesAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            var sql = SqlFor(db);

            var now = DateTimeOffset.UtcNow;
            var staleBefore = now - LeaseDuration;
            var running = nameof(JobStatus.Running);
            var transaction = await db.Database.BeginTransactionAsync(stoppingToken).ConfigureAwait(false);
            await using var transactionScope = transaction.ConfigureAwait(false);
            var expired = await db.Set<Job>()
                .FromSql(JobSql.Sql(
                    $$"""
                    SELECT * FROM {{sql.Table}}
                    WHERE {{sql.Status}} = {0}
                      AND ({{sql.LeaseExpiresAt}} < {1}
                           OR ({{sql.LeaseExpiresAt}} IS NULL AND {{sql.UpdatedAt}} < {2}))
                    ORDER BY {{sql.CreatedAt}}
                    LIMIT {3}
                    FOR UPDATE SKIP LOCKED
                    """,
                    running, now, staleBefore, RecoveryBatchSize))
                .ToListAsync(stoppingToken).ConfigureAwait(false);

            if (expired.Count == 0)
            {
                return;
            }

            foreach (var job in expired)
            {
                ApplyFailure(job, "Interrupted: lease expired while Running.");
            }
            await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
            await transaction.CommitAsync(stoppingToken).ConfigureAwait(false);

            JobWorkerLog.ExpiredLeasesRecovered(logger, expired.Count);
            foreach (var job in expired)
            {
                await AfterFailureRecordedAsync(job, exception: null).ConfigureAwait(false);
            }

            if (expired.Count < RecoveryBatchSize)
            {
                return;
            }
        }
    }

    private async Task WaitForWorkAsync(CancellationToken stoppingToken)
    {
        // The losing delay must not stay alive (up to the full poll interval) after a channel wake.
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var channelWait = wakeUp.Reader.WaitToReadAsync(waitCts.Token).AsTask();
        var timerWait = Task.Delay(SafetyNetPollInterval, waitCts.Token);
        await Task.WhenAny(channelWait, timerWait).ConfigureAwait(false);
        await waitCts.CancelAsync().ConfigureAwait(false);

        // Drain any queued signals now so the channel doesn't accumulate while this cycle runs —
        // the upcoming full sweep already covers whatever they were signaling.
        while (wakeUp.Reader.TryRead(out _))
        {
            // Discard: the sweep below handles every pending job.
        }
    }

    private async Task ProcessPendingJobsAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // One job per claim, so a long handler never keeps other jobs' leases ticking.
            var job = await ClaimNextPendingAsync(stoppingToken).ConfigureAwait(false);
            if (job is null)
            {
                return;
            }

            await ProcessJobAsync(job, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Atomically claims the oldest eligible Pending job: the row lock is held only for
    /// this short transaction; afterwards the Running status + lease protect the job.</summary>
    private async Task<Job?> ClaimNextPendingAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var sql = SqlFor(db);

        var now = DateTimeOffset.UtcNow;
        var pending = nameof(JobStatus.Pending);
        var transaction = await db.Database.BeginTransactionAsync(stoppingToken).ConfigureAwait(false);
        await using var transactionScope = transaction.ConfigureAwait(false);
        var job = (await db.Set<Job>()
            .FromSql(JobSql.Sql(
                $$"""
                SELECT * FROM {{sql.Table}}
                WHERE {{sql.Status}} = {0} AND ({{sql.NextAttemptAt}} IS NULL OR {{sql.NextAttemptAt}} <= {1})
                ORDER BY {{sql.CreatedAt}}
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """,
                pending, now))
            .ToListAsync(stoppingToken).ConfigureAwait(false)).SingleOrDefault();

        if (job is null)
        {
            return null;
        }

        job.Status = JobStatus.Running;
        job.LeaseOwner = _workerId;
        job.LeaseExpiresAt = now + LeaseDuration;
        job.UpdatedAt = now;
        await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
        await transaction.CommitAsync(stoppingToken).ConfigureAwait(false);
        return job;
    }

    private async Task ProcessJobAsync(Job job, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetServices<IJobHandler>()
            .ToDictionary(handler => handler.Type, StringComparer.Ordinal);

        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var renewal = RenewLeaseAsync(job.Id, renewalCts.Token);

        try
        {
            if (!handlers.TryGetValue(job.Type, out var handler))
            {
                throw new InvalidOperationException($"No {nameof(IJobHandler)} registered for {job.Type}.");
            }

            await handler.HandleAsync(job, stoppingToken).ConfigureAwait(false);
            await StopRenewalAsync(renewalCts, renewal).ConfigureAwait(false);

            // Non-cancellable inside TryFinishAsync: a handler that already ran to completion must
            // have its outcome durably recorded even if shutdown fires in this instant.
            if (await TryFinishAsync(job, JobStatus.Completed, attempts: null, lastError: null, nextAttemptAt: null).ConfigureAwait(false))
            {
                job.Status = JobStatus.Completed;
                JobWorkerLog.JobCompleted(logger, job.Id, job.Type);
                await NotifyObserversSafelyAsync(job, detail: null).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await StopRenewalAsync(renewalCts, renewal).ConfigureAwait(false);
            var outcome = ApplyFailure(job, ex.Message);
            if (await TryFinishAsync(job, outcome.Status, job.Attempts, job.LastError, outcome.NextAttemptAt).ConfigureAwait(false))
            {
                await AfterFailureRecordedAsync(job, ex).ConfigureAwait(false);
            }
        }
        finally
        {
            await StopRenewalAsync(renewalCts, renewal).ConfigureAwait(false);
        }
    }

    private static async Task StopRenewalAsync(CancellationTokenSource renewalCts, Task renewal)
    {
        await renewalCts.CancelAsync().ConfigureAwait(false);
        await renewal.ConfigureAwait(false);
    }

    /// <summary>Extends the lease at a third of its duration until cancelled. Failures are logged
    /// and retried on the next tick; a lost lease (0 rows) just stops renewing — the outcome write
    /// is ownership-checked, so the new owner's result is never overwritten.</summary>
    private async Task RenewLeaseAsync(Guid jobId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(LeaseDuration / 3);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<TContext>();
                    var now = DateTimeOffset.UtcNow;
                    var expiresAt = now + LeaseDuration;
                    var rows = await db.Set<Job>()
                        .Where(j => j.Id == jobId && j.LeaseOwner == _workerId && j.Status == JobStatus.Running)
                        .ExecuteUpdateAsync(
                            s => s.SetProperty(j => j.LeaseExpiresAt, expiresAt).SetProperty(j => j.UpdatedAt, now),
                            cancellationToken).ConfigureAwait(false);
                    if (rows == 0)
                    {
                        JobWorkerLog.LeaseLost(logger, jobId);
                        return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    JobWorkerLog.LeaseRenewalFailed(logger, ex, jobId);
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
        Job job, JobStatus status, int? attempts, string? lastError, DateTimeOffset? nextAttemptAt)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var now = DateTimeOffset.UtcNow;

        var query = db.Set<Job>().Where(j => j.Id == job.Id && j.LeaseOwner == _workerId);
        var rows = attempts is null
            ? await query.ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, status)
                    .SetProperty(j => j.LeaseOwner, (string?)null)
                    .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.UpdatedAt, now),
                CancellationToken.None).ConfigureAwait(false)
            : await query.ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, status)
                    .SetProperty(j => j.Attempts, attempts.Value)
                    .SetProperty(j => j.LastError, lastError)
                    .SetProperty(j => j.NextAttemptAt, nextAttemptAt)
                    .SetProperty(j => j.LeaseOwner, (string?)null)
                    .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                    .SetProperty(j => j.UpdatedAt, now),
                CancellationToken.None).ConfigureAwait(false);

        if (rows == 0)
        {
            JobWorkerLog.OutcomeDiscarded(logger, job.Id, job.Type);
        }

        return rows > 0;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct FailureOutcome(JobStatus Status, DateTimeOffset? NextAttemptAt);

    /// <summary>Applies the retry decision to the in-memory job (Attempts, LastError, Status,
    /// NextAttemptAt, cleared lease); persisting it is the caller's responsibility.</summary>
    private FailureOutcome ApplyFailure(Job job, string error)
    {
        var now = DateTimeOffset.UtcNow;
        job.Attempts++;
        job.LastError = error.Length > Job.LastErrorMaxLength ? error[..Job.LastErrorMaxLength] : error;
        job.Status = JobRetryPolicy.ShouldRetry(job.Attempts, options.Value.MaxAttempts) ? JobStatus.Pending : JobStatus.Failed;
        job.LeaseOwner = null;
        job.LeaseExpiresAt = null;
        job.UpdatedAt = now;

        if (job.Status == JobStatus.Pending)
        {
            job.NextAttemptAt = now + JobRetryPolicy.BackoffDelay(job.Attempts, options.Value.MaxBackoff);
        }

        return new FailureOutcome(job.Status, job.NextAttemptAt);
    }

    private async Task AfterFailureRecordedAsync(Job job, Exception? exception)
    {
        if (exception is not null)
        {
            JobWorkerLog.JobAttemptFailed(logger, exception, job.Id, job.Type, job.Attempts, job.Status);
        }
        else
        {
            JobWorkerLog.JobAttemptRecorded(logger, job.Id, job.Type, job.Attempts, job.LastError, job.Status);
        }

        await NotifyObserversSafelyAsync(job, job.LastError).ConfigureAwait(false);
    }

    /// <summary>Observer notifications are fire-and-forget, not the source of truth — a failure must never
    /// revert a job's already-persisted outcome, so each observer is isolated from the caller's try/catch
    /// and from the other observers.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Observer notifications are fire-and-forget; any failure is logged and must not revert the persisted job outcome.")]
    private async Task NotifyObserversSafelyAsync(Job job, string? detail)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            foreach (var observer in scope.ServiceProvider.GetServices<IJobObserver>())
            {
                try
                {
                    await observer.OnStateChangedAsync(job, detail, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    JobWorkerLog.ObserverFailed(logger, ex, job.Id);
                }
            }
        }
        catch (Exception ex)
        {
            JobWorkerLog.ObserverFailed(logger, ex, job.Id);
        }
    }

    /// <summary>End of every sweep: one cheap query for the earliest lease expiry among Running jobs
    /// and the earliest NextAttemptAt among Pending jobs (a dead worker's lease, or a backoff whose
    /// in-process signal died with the old process), then a single scheduled wake at
    /// min(that + margin, safety-net poll). Replaces the previous scheduled wake, so at most one is
    /// pending; nothing is scheduled when nothing is waiting.</summary>
    private async Task ScheduleNextWakeAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var sql = SqlFor(db);
        var running = nameof(JobStatus.Running);
        var pending = nameof(JobStatus.Pending);

        var earliest = await db.Database
            .SqlQuery<DateTimeOffset?>(JobSql.Sql(
                $$"""
                SELECT LEAST(
                    (SELECT MIN({{sql.LeaseExpiresAt}}) FROM {{sql.Table}} WHERE {{sql.Status}} = {0}),
                    (SELECT MIN({{sql.NextAttemptAt}}) FROM {{sql.Table}} WHERE {{sql.Status}} = {1})) AS "Value"
                """,
                running, pending))
            .SingleAsync(stoppingToken).ConfigureAwait(false);

        if (earliest is null)
        {
            ReplaceWake(null);
            return;
        }
        var delay = earliest.Value - DateTimeOffset.UtcNow + JobWorkerTiming.WakeMargin;
        if (delay < JobWorkerTiming.WakeMargin)
        {
            delay = JobWorkerTiming.WakeMargin;
        }

        if (delay > SafetyNetPollInterval)
        {
            delay = SafetyNetPollInterval;
        }

        StartWake(delay, stoppingToken);
    }

    private JobSql SqlFor(TContext db) => _sql ??= JobSql.From(db);

    private void ReplaceWake(CancellationTokenSource? next)
    {
        var previous = _scheduledWake;
        _scheduledWake = next;
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }
    }

    /// <summary>Fire-and-forget wake after <paramref name="delay"/>; replaces any previous one.</summary>
    private void StartWake(TimeSpan delay, CancellationToken stoppingToken)
    {
        var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var token = wake.Token;
        ReplaceWake(wake);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
                wakeUp.Signal();
            }
            catch (OperationCanceledException)
            {
                // Replaced by a newer schedule, or the host is shutting down.
            }
        }, CancellationToken.None);
    }
}

internal static class JobWorkerTiming
{
    /// <summary>First retry delay after a failed sweep; doubles per consecutive failure.</summary>
    public static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);

    /// <summary>Slack added to a scheduled wake so the row it targets is already due when the sweep runs.</summary>
    public static readonly TimeSpan WakeMargin = TimeSpan.FromSeconds(1);
}
