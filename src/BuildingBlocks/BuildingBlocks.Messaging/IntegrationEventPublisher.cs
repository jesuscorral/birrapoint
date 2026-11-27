using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Messaging;

internal sealed class IntegrationEventPublisher<TContext>(TContext db, MessagingRegistry registry, TimeProvider time)
    : IIntegrationEventPublisher
    where TContext : DbContext
{
    public Guid Publish<TEvent>(TEvent integrationEvent)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var outbox = registry.OutboxFor(typeof(TEvent))
            ?? throw new InvalidOperationException(
                $"{typeof(TEvent).FullName} is not registered for publishing; call Publishes<{typeof(TEvent).Name}>(outbox) in AddMessaging.");
        var descriptor = registry.DescriptorFor(typeof(TEvent))!;

        var envelope = EventSerializer.ToEnvelope(descriptor, integrationEvent, Guid.CreateVersion7(), time.GetUtcNow());
        db.Set<OutboxMessage>(MessagingNames.Outbox(outbox)).Add(new OutboxMessage
        {
            Id = envelope.Id,
            Type = envelope.Type,
            Version = envelope.Version,
            OccurredAt = envelope.OccurredAt,
            PayloadJson = envelope.PayloadJson,
        });
        return envelope.Id;
    }
}
