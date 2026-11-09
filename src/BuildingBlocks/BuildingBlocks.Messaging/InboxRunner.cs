using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Runs one handler for one delivery. A new scope and transaction wrap three steps: insert the inbox row
/// (<c>ON CONFLICT DO NOTHING</c>), call the handler only if the row was new, commit both together. A handler
/// exception rolls the inbox row back too, so the retry runs the handler again; a redelivery of a processed
/// event finds the row and does nothing (FR-079). After-commit callbacks run last and never after a rollback.
/// </summary>
internal sealed class InboxRunner<TContext>(
    IServiceScopeFactory scopeFactory,
    TimeProvider time,
    ILogger<InboxRunner<TContext>> logger)
    where TContext : DbContext
{
    public async Task RunAsync(Subscription subscription, IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        var context = await ProcessAsync(subscription, envelope, cancellationToken).ConfigureAwait(false);

        if (context is not null)
        {
            await RunCallbacksAsync(subscription, envelope, context, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IntegrationEventContext?> ProcessAsync(
        Subscription subscription, IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var inbox = InboxSql.From(db, subscription.InboxName);

        var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var transactionScope = transaction.ConfigureAwait(false);

        var inserted = await db.Database.ExecuteSqlAsync(
            SqlText.Sql(
                $$"""
                INSERT INTO {{inbox.Table}} ({{inbox.EventId}}, {{inbox.Handler}}, {{inbox.ProcessedAt}})
                VALUES ({0}, {1}, {2})
                ON CONFLICT DO NOTHING
                """,
                envelope.Id, subscription.HandlerName, time.GetUtcNow()),
            cancellationToken).ConfigureAwait(false);

        if (inserted == 0)
        {
            // Already processed: the effect is in place.
            return null;
        }

        var context = new IntegrationEventContext(envelope.Id, envelope.OccurredAt);
        var handler = scope.ServiceProvider.GetRequiredService(subscription.HandlerType);
        var payload = EventSerializer.Deserialize(subscription.Event, envelope.PayloadJson);
        await subscription.Invoke(handler, payload, context, cancellationToken).ConfigureAwait(false);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return context;
    }

    /// <summary>The effect is committed; a callback failure (an emit that did not go out) is logged and never
    /// fails the delivery, otherwise the retry would find the inbox row and skip the callback anyway.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "After-commit callbacks are best effort; any failure is logged and must not fail the committed delivery.")]
    private async Task RunCallbacksAsync(
        Subscription subscription, IntegrationEventEnvelope envelope, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        foreach (var callback in context.AfterCommit)
        {
            try
            {
                await callback(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                MessagingLog.AfterCommitCallbackFailed(logger, ex, envelope.Type, envelope.Id, subscription.HandlerName);
            }
        }
    }
}
