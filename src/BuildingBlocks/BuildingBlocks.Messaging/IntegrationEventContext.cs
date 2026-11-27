namespace BuildingBlocks.Messaging;

/// <summary>What a handler knows about the delivery it is processing.</summary>
public sealed class IntegrationEventContext
{
    private readonly List<Func<CancellationToken, Task>> _afterCommit = [];

    public IntegrationEventContext(Guid eventId, DateTimeOffset occurredAt)
    {
        EventId = eventId;
        OccurredAt = occurredAt;
    }

    public Guid EventId { get; }

    public DateTimeOffset OccurredAt { get; }

    internal IReadOnlyList<Func<CancellationToken, Task>> AfterCommit => _afterCommit;

    /// <summary>Registers work to run once the handler's transaction has committed (realtime emits, job wake-up).
    /// It never runs when the handler throws or the transaction rolls back. A failing callback is logged and
    /// does not undo the committed effect, so callbacks must be best effort.</summary>
    public void OnCommitted(Func<CancellationToken, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _afterCommit.Add(callback);
    }
}
