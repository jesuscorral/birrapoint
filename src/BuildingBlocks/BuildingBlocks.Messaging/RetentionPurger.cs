using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Bounded growth for the outbox and inbox tables (FR-090): Delivered outbox rows after
/// <see cref="MessagingOptions.OutboxRetention"/>, Dead ones after <see cref="MessagingOptions.DeadRetention"/>,
/// inbox rows after <see cref="MessagingOptions.InboxRetention"/>. Pending and Delivering rows are never touched.
/// Deletes go in batches so locks stay short. The dispatcher calls <see cref="RunIfDueAsync"/> from its sweep, so
/// the purge adds no wake-up of its own.
/// </summary>
internal sealed class RetentionPurger<TContext>(
    IServiceScopeFactory scopeFactory,
    MessagingRegistry registry,
    IOptions<MessagingOptions> options,
    TimeProvider time,
    ILogger<RetentionPurger<TContext>> logger)
    where TContext : DbContext
{
    private DateTimeOffset? _lastRun;

    /// <summary>Runs one pass unless the previous one was less than <see cref="MessagingOptions.PurgeInterval"/> ago.
    /// Returns whether a pass ran.</summary>
    public async Task<bool> RunIfDueAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        if (_lastRun is { } last && now - last < settings.PurgeInterval)
        {
            return false;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        foreach (var outbox in registry.GetOutboxNames())
        {
            var sql = OutboxSql.From(db, outbox);
            var delivered = await DeleteOutboxAsync(db, sql, OutboxStatus.Delivered, now - settings.OutboxRetention, settings.PurgeBatchSize, cancellationToken).ConfigureAwait(false);
            var dead = await DeleteOutboxAsync(db, sql, OutboxStatus.Dead, now - settings.DeadRetention, settings.PurgeBatchSize, cancellationToken).ConfigureAwait(false);
            MessagingLog.OutboxPurged(logger, delivered, dead, sql.Display);
        }

        foreach (var inbox in registry.GetInboxNames())
        {
            var sql = InboxSql.From(db, inbox);
            var count = await DeleteInboxAsync(db, sql, now - settings.InboxRetention, settings.PurgeBatchSize, cancellationToken).ConfigureAwait(false);
            MessagingLog.InboxPurged(logger, count, sql.Display);
        }

        _lastRun = now;
        return true;
    }

    private static async Task<int> DeleteOutboxAsync(
        DbContext db, OutboxSql sql, OutboxStatus status, DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        int deleted;
        do
        {
            deleted = await db.Database.ExecuteSqlAsync(
                SqlText.Sql(
                    $$"""
                    DELETE FROM {{sql.Table}}
                    WHERE ctid IN (
                        SELECT ctid FROM {{sql.Table}}
                        WHERE {{sql.Status}} = {0} AND {{sql.OccurredAt}} < {1}
                        LIMIT {2})
                    """,
                    status.ToString(), cutoff, batchSize),
                cancellationToken).ConfigureAwait(false);
            total += deleted;
        }
        while (deleted >= batchSize);

        return total;
    }

    private static async Task<int> DeleteInboxAsync(
        DbContext db, InboxSql sql, DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken)
    {
        var total = 0;
        int deleted;
        do
        {
            deleted = await db.Database.ExecuteSqlAsync(
                SqlText.Sql(
                    $$"""
                    DELETE FROM {{sql.Table}}
                    WHERE ctid IN (
                        SELECT ctid FROM {{sql.Table}}
                        WHERE {{sql.ProcessedAt}} < {0}
                        LIMIT {1})
                    """,
                    cutoff, batchSize),
                cancellationToken).ConfigureAwait(false);
            total += deleted;
        }
        while (deleted >= batchSize);

        return total;
    }
}
