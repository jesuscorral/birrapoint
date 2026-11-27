using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Jobs;

internal static partial class JobWorkerLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Job worker sweep failed ({Failures} in a row); retrying in {Delay}.")]
    public static partial void SweepFailed(ILogger logger, int failures, TimeSpan delay);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Job worker cycle failed; a retry wake is scheduled.")]
    public static partial void CycleFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Recovered {Count} job(s) with an expired lease.")]
    public static partial void ExpiredLeasesRecovered(ILogger logger, int count);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Job {JobId} ({Type}) completed.")]
    public static partial void JobCompleted(ILogger logger, Guid jobId, string type);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Job {JobId} lease was lost; stopping renewal.")]
    public static partial void LeaseLost(ILogger logger, Guid jobId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Failed to renew the lease of job {JobId}; will retry.")]
    public static partial void LeaseRenewalFailed(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning,
        Message = "Job {JobId} ({Type}) lost its lease before the outcome was recorded; result discarded.")]
    public static partial void OutcomeDiscarded(ILogger logger, Guid jobId, string type);

    [LoggerMessage(EventId = 8, Level = LogLevel.Error,
        Message = "Job {JobId} ({Type}) failed attempt {Attempts}; next status {Status}.")]
    public static partial void JobAttemptFailed(
        ILogger logger, Exception exception, Guid jobId, string type, int attempts, JobStatus status);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning,
        Message = "Job {JobId} ({Type}) recorded as a failed attempt {Attempts} ({Reason}); next status {Status}.")]
    public static partial void JobAttemptRecorded(
        ILogger logger, Guid jobId, string type, int attempts, string? reason, JobStatus status);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning, Message = "A job observer failed for job {JobId}.")]
    public static partial void ObserverFailed(ILogger logger, Exception exception, Guid jobId);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information,
        Message = "Job {JobId} ({Type}) deferred without using an attempt; next run at {NextAttemptAt}.")]
    public static partial void JobDeferred(ILogger logger, Guid jobId, string type, DateTimeOffset nextAttemptAt);
}
