namespace BuildingBlocks.Messaging;

/// <summary>
/// A subscriber of <typeparamref name="TEvent"/>. It runs in its own scope and transaction, together with its
/// inbox row, so its effect happens once however often the event is delivered. It must converge to the same
/// state whatever the delivery order, and do nothing when its target is gone. Realtime emits and other work that
/// must follow the commit go through <see cref="IntegrationEventContext.OnCommitted"/>.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Public name fixed by the integration-events contract; it is not a CLR event handler.")]
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : class
{
    Task HandleAsync(TEvent integrationEvent, IntegrationEventContext context, CancellationToken cancellationToken);
}
