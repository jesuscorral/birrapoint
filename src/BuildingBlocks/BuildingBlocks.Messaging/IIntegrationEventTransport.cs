namespace BuildingBlocks.Messaging;

/// <summary>
/// How a dispatched envelope reaches its subscribers. The in-process transport runs the handlers directly; a
/// broker transport publishes to a topic and succeeds once the broker accepts the message. Publishers and
/// handlers do not change when the transport does. Register another implementation after
/// <c>AddMessaging</c> to replace the default.
/// </summary>
public interface IIntegrationEventTransport
{
    /// <summary>Returns when the envelope was accepted by every subscriber (or the broker). Throw
    /// <see cref="IntegrationEventDeliveryException"/> to fail the delivery: it is retried with backoff, and
    /// marked Dead after the last attempt.</summary>
    Task SendAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken);
}

/// <summary>A delivery that failed because one or more subscribers failed, or because nobody can ever handle
/// the event.</summary>
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

    public IntegrationEventDeliveryException(string message, IReadOnlyList<string> failedHandlers, bool permanent = false)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(failedHandlers);
        FailedHandlers = failedHandlers;
        Permanent = permanent;
    }

    /// <summary>Short type names of the handlers that failed; safe to log.</summary>
    public IReadOnlyList<string> FailedHandlers { get; }

    /// <summary>True when retrying cannot help (for example an unknown event type): the event goes Dead at once.</summary>
    public bool Permanent { get; }
}
