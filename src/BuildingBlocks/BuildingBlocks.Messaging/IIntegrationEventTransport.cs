namespace BuildingBlocks.Messaging;

/// <summary>
/// How a dispatched envelope reaches its subscribers. The in-process transport runs the handlers directly; a
/// broker transport publishes to a topic and succeeds once the broker accepts the message. Publishers and
/// handlers do not change when the transport does. Register another implementation after
/// <c>AddMessaging</c> to replace the default.
/// </summary>
public interface IIntegrationEventTransport
{
    /// <summary>Returns <see cref="DeliveryOutcome.Delivered"/> when the envelope was accepted by every subscriber
    /// (or the broker), or <see cref="DeliveryOutcome.Deferred"/> when this build cannot handle it yet. Throw
    /// <see cref="IntegrationEventDeliveryException"/> to fail the delivery: it is retried with backoff, and
    /// marked Dead after the last attempt.</summary>
    Task<DeliveryOutcome> SendAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken);
}

/// <summary>What the transport did with an envelope.</summary>
public enum DeliveryOutcome
{
    /// <summary>Every subscriber (or the broker) accepted the event.</summary>
    Delivered,

    /// <summary>This build does not know the event (for example after a rollback to an older build): it stays
    /// pending and is offered again after the maximum backoff, without using up an attempt, so a build that
    /// knows it still delivers it.</summary>
    Deferred,
}

/// <summary>A delivery that failed because one or more subscribers failed.</summary>
public sealed class IntegrationEventDeliveryException : Exception
{
    public IntegrationEventDeliveryException()
        : this("Integration event delivery failed.", [])
    {
    }

    public IntegrationEventDeliveryException(string message)
        : this(message, [])
    {
    }

    public IntegrationEventDeliveryException(string message, Exception innerException)
        : base(message, innerException)
    {
        FailedHandlers = [];
    }

    public IntegrationEventDeliveryException(string message, IReadOnlyList<string> failedHandlers)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(failedHandlers);
        FailedHandlers = failedHandlers;
    }

    /// <summary>Short type names of the handlers that failed; safe to log.</summary>
    public IReadOnlyList<string> FailedHandlers { get; }
}
