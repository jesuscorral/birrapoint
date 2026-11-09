using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging;

/// <summary>Log messages carry the event type, the event id and subscriber names only; never the payload.</summary>
internal static partial class MessagingLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Outbox dispatcher sweep failed ({Failures} in a row); retrying in {Delay}.")]
    public static partial void SweepFailed(ILogger logger, int failures, TimeSpan delay);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Outbox dispatcher cycle failed; a retry wake is scheduled.")]
    public static partial void CycleFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Outbox dispatcher is disabled by configuration; events stay in the outbox.")]
    public static partial void DispatcherDisabled(ILogger logger);

    [LoggerMessage(EventId = 4, Level = LogLevel.Debug, Message = "Integration event {Type} {EventId} delivered.")]
    public static partial void Delivered(ILogger logger, string type, Guid eventId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
        Message = "Delivery of integration event {Type} {EventId} failed (attempt {Attempts}); retry at {NextAttemptAt}. Failed subscribers: {Subscribers}.")]
    public static partial void DeliveryFailedWillRetry(
        ILogger logger, string type, Guid eventId, int attempts, DateTimeOffset nextAttemptAt, string subscribers);

    [LoggerMessage(EventId = 6, Level = LogLevel.Error,
        Message = "Integration event {Type} {EventId} is dead after {Attempts} attempt(s). Failed subscribers: {Subscribers}.")]
    public static partial void EventDead(ILogger logger, string type, Guid eventId, int attempts, string subscribers);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning,
        Message = "Integration event {Type} {EventId} lost its lease before the outcome was recorded; result discarded.")]
    public static partial void OutcomeDiscarded(ILogger logger, string type, Guid eventId);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Integration event {EventId} lease was lost; stopping renewal.")]
    public static partial void LeaseLost(ILogger logger, Guid eventId);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "Failed to renew the lease of integration event {EventId}; will retry.")]
    public static partial void LeaseRenewalFailed(ILogger logger, Exception exception, Guid eventId);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
        Message = "After-commit callback of {Subscriber} failed for integration event {Type} {EventId}; the handler's effect is committed.")]
    public static partial void AfterCommitCallbackFailed(ILogger logger, Exception exception, string type, Guid eventId, string subscriber);

    [LoggerMessage(EventId = 11, Level = LogLevel.Information, Message = "Purged {Delivered} delivered and {Dead} dead outbox message(s) from {Table}.")]
    public static partial void OutboxPurged(ILogger logger, int delivered, int dead, string table);

    [LoggerMessage(EventId = 12, Level = LogLevel.Information, Message = "Purged {Count} inbox message(s) from {Table}.")]
    public static partial void InboxPurged(ILogger logger, int count, string table);
}
