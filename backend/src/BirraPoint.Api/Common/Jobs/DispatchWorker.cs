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
/// Tracked gap (same pattern as T015's CompetitionHub): the DB-backed resume-sweep and dispatch
/// loop below have no integration test yet — T018's Testcontainers harness doesn't exist, and the
/// constitution bans EF Core's InMemory provider as a substitute. Only DispatchRetryPolicy (pure)
/// is unit-tested for now; close this gap once T018 lands.
/// </summary>
public sealed class DispatchWorker(
    IServiceScopeFactory scopeFactory, Channel<Guid> wakeUpChannel, IEventPublisher eventPublisher)
    : BackgroundService
{
    private static readonly TimeSpan SafetyNetPollInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ResumeInterruptedJobsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await WaitForWorkAsync(stoppingToken);

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await ProcessPendingJobsAsync(stoppingToken);
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
        // the upcoming full Pending sweep already covers whatever they were signaling.
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

        var pendingJobs = await db.DispatchJobs
            .Where(job => job.Status == DispatchJobStatus.Pending)
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
        await db.SaveChangesAsync(stoppingToken);

        try
        {
            if (!handlers.TryGetValue(job.Type, out var handler))
            {
                throw new InvalidOperationException($"No {nameof(IDispatchJobHandler)} registered for {job.Type}.");
            }

            await handler.HandleAsync(job, stoppingToken);

            job.Status = DispatchJobStatus.Completed;
            await db.SaveChangesAsync(stoppingToken);
            await eventPublisher.PublishToOrganizersAsync(
                job.CompetitionId, CompetitionEvents.DispatchProgress,
                new DispatchProgressPayload(job.Type, job.Status, Detail: null), CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordFailureAsync(job, db, ex.Message, stoppingToken);
        }
    }

    private async Task RecordFailureAsync(DispatchJob job, AppDbContext db, string error, CancellationToken stoppingToken)
    {
        job.Attempts++;
        job.LastError = error;
        job.Status = DispatchRetryPolicy.ShouldRetry(job.Attempts) ? DispatchJobStatus.Pending : DispatchJobStatus.Failed;
        await db.SaveChangesAsync(stoppingToken);

        await eventPublisher.PublishToOrganizersAsync(
            job.CompetitionId, CompetitionEvents.DispatchProgress,
            new DispatchProgressPayload(job.Type, job.Status, job.LastError), CancellationToken.None);

        if (job.Status == DispatchJobStatus.Pending)
        {
            ScheduleRetrySignal(job.Id, DispatchRetryPolicy.BackoffDelay(job.Attempts), stoppingToken);
        }
    }

    /// <summary>Best-effort early wake-up for a backed-off retry; the periodic safety-net poll in
    /// <see cref="WaitForWorkAsync"/> still covers this job if the delayed write is ever missed.</summary>
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
