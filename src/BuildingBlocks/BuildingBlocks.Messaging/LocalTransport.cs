using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Messaging;

/// <summary>In-process transport: runs every subscriber of the event through the <see cref="InboxRunner{TContext}"/>
/// and fails if any subscriber fails, naming them. The inbox makes re-running the subscribers that already
/// succeeded a no-op, so one delivery state per event is enough (R-106-5).</summary>
internal sealed class LocalTransport<TContext>(MessagingRegistry registry, InboxRunner<TContext> inboxRunner)
    : IIntegrationEventTransport
    where TContext : DbContext
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "A failing subscriber must not stop the others; every failure is collected and reported together.")]
    public async Task SendAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!registry.TryGetDescriptor(envelope.Type, envelope.Version, out var descriptor))
        {
            throw new IntegrationEventDeliveryException(
                $"Unknown integration event '{envelope.Type}' version {envelope.Version}.", [], permanent: true);
        }

        var failedHandlers = new List<string>();
        var failures = new List<string>();
        foreach (var subscription in registry.SubscriptionsFor(descriptor))
        {
            try
            {
                await inboxRunner.RunAsync(subscription, envelope, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failedHandlers.Add(subscription.HandlerName);
                failures.Add($"{subscription.HandlerName}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            throw new IntegrationEventDeliveryException($"Handlers failed: {string.Join("; ", failures)}", failedHandlers);
        }
    }
}
