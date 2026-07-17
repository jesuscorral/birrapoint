using System.Threading.Channels;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Realtime;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Common.Jobs;

/// <summary>
/// Hosted worker for the <see cref="DispatchJob"/> queue (T016/R-06). Wakes on
/// <paramref name="wakeUpChannel"/> writes from <see cref="DispatchJobQueue"/>, with a periodic
/// safety-net poll so a missed signal or a backoff-delayed retry is never lost. Every job is
/// resolved through a fresh DI scope (AppDbContext is scoped; this worker is a singleton).
///
/// Every cycle runs inside <see cref="RunGuardedAsync"/>: the default
/// <c>BackgroundServiceExceptionBehavior</c> is <c>StopHost</c>, so an unguarded transient DB
/// error here would take down the entire API — the opposite of R-06's "survive restarts" premise.
///
/// Tracked gap (same pattern as T015's CompetitionHub): the DB-backed resume-sweep and dispatch
/// loop below have no integration test yet — T018's Testcontainers harness doesn't exist, and the
/// constitution bans EF Core's InMemory provider as a substitute. Only DispatchRetryPolicy (pure)
/// is unit-tested for now; close this gap once T018 lands.
/// </summary>
public sealed class DispatchWorker(
    IServiceScopeFactory scopeFactory,
    Channel<Guid> wakeUpChannel,
    IEventPublisher eventPublisher,
    ILogger<DispatchWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan SafetyNetPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);
    private const int LastErrorMaxLength = 2000; // matches DispatchJobConfiguration.LastError

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunGuardedAsync(ResumeInterruptedJobsAsync, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await WaitForWorkAsync(stoppingToken);

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

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

    /// <summary>A job still `Running` at startup means the process crashed mid-handler; treat it
    /// exactly like a failed attempt so it goes through the same retry/backoff decision.</summary>
    private async Task ResumeInterruptedJobsAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var interrupted = await db.DispatchJobs
            .Where(job => job.Status == DispatchJobStatus.Running)
            .ToListAsync(stoppingToken);

        if (interrupted.Count > 0)
        {
            logger.LogWarning(
                "Resuming {Count} DispatchJob(s) left Running by a previous process instance.", interrupted.Count);
        }

        foreach (var job in interrupted)
        {
            await RecordFailureAsync(job, db, "Interrupted by a process restart while Running.", stoppingToken);
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
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handlers = scope.ServiceProvider.GetServices<IDispatchJobHandler>()
            .ToDictionary(handler => handler.Type);

        var now = DateTimeOffset.UtcNow;
        var pendingJobs = await db.DispatchJobs
            .Where(job => job.Status == DispatchJobStatus.Pending && (job.NextAttemptAt == null || job.NextAttemptAt <= now))
            .OrderBy(job => job.CreatedAt)
            .ToListAsync(stoppingToken);

        foreach (var job in pendingJobs)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await ProcessJobAsync(job, db, handlers, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(
        DispatchJob job, AppDbContext db, IReadOnlyDictionary<DispatchJobType, IDispatchJobHandler> handlers,
        CancellationToken stoppingToken)
    {
        job.Status = DispatchJobStatus.Running;
        // Left outside the try deliberately: a failure marking the job Running is an infra
        // problem (DB blip), not a failed attempt of the job itself — it propagates to
        // RunGuardedAsync's cycle-level guard instead of burning into this job's Attempts budget.
        await db.SaveChangesAsync(stoppingToken);

        try
        {
            if (!handlers.TryGetValue(job.Type, out var handler))
            {
                throw new InvalidOperationException($"No {nameof(IDispatchJobHandler)} registered for {job.Type}.");
            }

            await handler.HandleAsync(job, stoppingToken);

            job.Status = DispatchJobStatus.Completed;
            // Non-cancellable: a handler that already ran to completion must have its outcome
            // durably recorded even if shutdown fires in this instant — otherwise the next
            // startup's resume-sweep would treat a genuinely finished job as a crashed one.
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogInformation("DispatchJob {JobId} ({Type}) completed.", job.Id, job.Type);

            await PublishProgressSafely(job, detail: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordFailureAsync(job, db, ex.Message, stoppingToken, ex);
        }
    }

    private async Task RecordFailureAsync(
        DispatchJob job, AppDbContext db, string error, CancellationToken stoppingToken, Exception? exception = null)
    {
        job.Attempts++;
        job.LastError = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;
        job.Status = DispatchRetryPolicy.ShouldRetry(job.Attempts) ? DispatchJobStatus.Pending : DispatchJobStatus.Failed;

        TimeSpan? backoff = null;
        if (job.Status == DispatchJobStatus.Pending)
        {
            backoff = DispatchRetryPolicy.BackoffDelay(job.Attempts);
            job.NextAttemptAt = DateTimeOffset.UtcNow + backoff.Value;
        }

        // Non-cancellable for the same reason as the Completed save above: this outcome must
        // persist even mid-shutdown, or the job gets double-counted as a crash next startup.
        await db.SaveChangesAsync(CancellationToken.None);

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
                job.Id, job.Type, job.Attempts, error, job.Status);
        }

        await PublishProgressSafely(job, job.LastError);

        if (backoff.HasValue)
        {
            ScheduleRetrySignal(job.Id, backoff.Value, stoppingToken);
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
    /// <see cref="ProcessPendingJobsAsync"/>, is what actually enforces the delay — this only
    /// saves the job from waiting out the full 30s poll interval unnecessarily.</summary>
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
