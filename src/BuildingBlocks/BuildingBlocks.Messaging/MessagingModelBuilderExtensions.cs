using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Messaging;

public static class MessagingModelBuilderExtensions
{
    /// <summary>
    /// Maps one module's outbox: <c>&lt;schema&gt;."OutboxMessages"</c>, as a shared-type entity named
    /// <paramref name="name"/> (the same name is given to <see cref="MessagingBuilder.Publishes{TEvent}"/>).
    /// <see cref="OutboxMessage.Status"/> is stored as a string, the payload as <c>jsonb</c>, and the two indexes
    /// the dispatcher's claim and wake-up queries use are created. The dispatcher reads table and column names
    /// from this model, so a host may rename them with its own configuration afterwards.
    /// </summary>
    public static ModelBuilder ApplyOutbox(this ModelBuilder modelBuilder, string name, string schema)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        modelBuilder.SharedTypeEntity<OutboxMessage>(MessagingNames.Outbox(name), outbox =>
        {
            outbox.ToTable("OutboxMessages", schema);
            outbox.HasKey(m => m.Id).HasName($"PK_{schema}_OutboxMessages");
            outbox.Property(m => m.Id).ValueGeneratedNever();
            outbox.Property(m => m.Type).HasMaxLength(IntegrationEventAttribute.TypeMaxLength);
            outbox.Property(m => m.PayloadJson).HasColumnType("jsonb");
            outbox.Property(m => m.Status).HasConversion<string>().HasMaxLength(OutboxMessage.StatusMaxLength);
            outbox.Property(m => m.LeaseOwner).HasMaxLength(OutboxMessage.LeaseOwnerMaxLength);
            outbox.Property(m => m.LastError).HasMaxLength(OutboxMessage.LastErrorMaxLength);

            // Claim: Status == Pending && NextAttemptAt <= now; recovery: Status == Delivering && LeaseExpiresAt < now.
            // Names carry the schema: the same two indexes exist once per module.
            outbox.HasIndex(m => new { m.Status, m.NextAttemptAt }).HasDatabaseName($"IX_{schema}_OutboxMessages_Status_NextAttemptAt");
            outbox.HasIndex(m => new { m.Status, m.LeaseExpiresAt }).HasDatabaseName($"IX_{schema}_OutboxMessages_Status_LeaseExpiresAt");
        });

        return modelBuilder;
    }

    /// <summary>
    /// Maps one module's inbox: <c>&lt;schema&gt;."InboxMessages"</c> with the composite key
    /// (<see cref="InboxMessage.EventId"/>, <see cref="InboxMessage.Handler"/>), as a shared-type entity named
    /// <paramref name="name"/> (the same name is given to <see cref="MessagingBuilder.Subscribe{TEvent, THandler}"/>).
    /// </summary>
    public static ModelBuilder ApplyInbox(this ModelBuilder modelBuilder, string name, string schema)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        modelBuilder.SharedTypeEntity<InboxMessage>(MessagingNames.Inbox(name), inbox =>
        {
            inbox.ToTable("InboxMessages", schema);
            inbox.HasKey(m => new { m.EventId, m.Handler }).HasName($"PK_{schema}_InboxMessages");
            inbox.Property(m => m.Handler).HasMaxLength(InboxMessage.HandlerMaxLength);

            // Retention purge: ProcessedAt < cutoff.
            inbox.HasIndex(m => m.ProcessedAt).HasDatabaseName($"IX_{schema}_InboxMessages_ProcessedAt");
        });

        return modelBuilder;
    }
}
