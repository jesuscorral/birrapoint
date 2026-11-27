namespace BuildingBlocks.Messaging;

/// <summary>
/// Records that a handler already processed an event. The row is inserted in the same transaction as the
/// handler's effect, so a redelivery finds it and does nothing. Mapped as a shared-type entity, once per
/// subscribing module schema, by <see cref="MessagingModelBuilderExtensions.ApplyInbox"/>.
/// </summary>
public sealed class InboxMessage
{
    public const int HandlerMaxLength = 200;

    public Guid EventId { get; set; }

    /// <summary>Short type name of the handler class.</summary>
    public required string Handler { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}
