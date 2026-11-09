namespace BuildingBlocks.Messaging;

/// <summary>
/// One integration event stored in a module's outbox, in the publisher's transaction. The row is the envelope
/// (id, type, version, time, payload) plus the delivery state. Mapped as a shared-type entity, once per module
/// schema, by <see cref="MessagingModelBuilderExtensions.ApplyOutbox"/>.
/// </summary>
public sealed class OutboxMessage
{
    public const int StatusMaxLength = 20;
    public const int LeaseOwnerMaxLength = 200;
    public const int LastErrorMaxLength = 2000;

    /// <summary>Sequential (v7) Guid; it is the event id receivers de-duplicate on.</summary>
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Type { get; set; }

    public int Version { get; set; } = 1;

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Event payload as JSON (<c>jsonb</c>). Identifiers only, never personal data.</summary>
    public required string PayloadJson { get; set; }

    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>Failed delivery attempts so far.</summary>
    public int Attempts { get; set; }

    /// <summary>Null until a failed attempt schedules a backoff-delayed retry; a Pending row is due when this is
    /// null or past.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LeaseOwner { get; set; }

    /// <summary>A Delivering row whose lease has expired belongs to a crashed dispatcher and is claimed again.</summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    /// <summary>Failing subscribers and exception type/message of the last failure; never the payload.</summary>
    public string? LastError { get; set; }
}

/// <summary><c>Pending</c> to <c>Delivering</c> to <c>Delivered</c>; <c>Dead</c> after the last failed attempt
/// (a failed attempt before that returns the row to <c>Pending</c>).</summary>
public enum OutboxStatus
{
    Pending,
    Delivering,
    Delivered,
    Dead,
}
