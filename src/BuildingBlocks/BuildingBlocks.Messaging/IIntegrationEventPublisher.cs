namespace BuildingBlocks.Messaging;

/// <summary>Publishes integration events from inside a module.</summary>
public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Stages the event in the publishing module's outbox on the caller's <c>DbContext</c> change tracker and
    /// returns the event id. Nothing is saved: the row commits or rolls back with the caller's own
    /// <c>SaveChangesAsync</c> or transaction, which makes the state change and its event atomic. Throws when
    /// <typeparamref name="TEvent"/> was not registered with <see cref="MessagingBuilder.Publishes{TEvent}"/>.
    /// </summary>
    Guid Publish<TEvent>(TEvent integrationEvent)
        where TEvent : class;
}
