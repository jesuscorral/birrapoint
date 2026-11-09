using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Hosted dispatcher for every registered outbox (PostgreSQL only). Wakes on <see cref="OutboxWakeUp"/> signals,
/// with a periodic safety-net poll so a missed signal, a backoff-delayed retry or an expired lease is never lost.
///
/// Safe with several instances (rolling deployments, replicas): a row is claimed atomically
/// (<c>FOR UPDATE SKIP LOCKED</c> in one transaction that also marks it Delivering with
/// <see cref="OutboxMessage.LeaseOwner"/>/<see cref="OutboxMessage.LeaseExpiresAt"/>), the lease is renewed while
/// the transport works, and the outcome is written only <c>WHERE LeaseOwner = me</c>. A Delivering row whose lease
/// expired (crashed dispatcher) is claimed again and counts as a failed attempt.
///
/// Each cycle is guarded: the default background-service exception behavior stops the host, and a transient DB
/// fault here must not do that.
/// </summary>
internal sealed class OutboxDispatcher<TContext>(
    IServiceScopeFactory scopeFactory,
    OutboxWakeUp wakeUp,
    MessagingRegistry registry,
    IIntegrationEventTransport transport,
    RetentionPurger<TContext> purger,
    IOptions<MessagingOptions> options,
    TimeProvider time,
    ILogger<OutboxDispatcher<TContext>> logger)
    : BackgroundService
    where TContext : DbContext
{
    /// <summary>Unique per process, so two replicas never share a lease identity.</summary>
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    private readonly Dictionary<string, OutboxSql> _sql = new(StringComparer.Ordinal);
    private int _consecutiveFailedSweeps;
    private CancellationTokenSource? _scheduledWake;

    private TimeSpan LeaseDuration => options.Value.LeaseDuration;

    private TimeSpan SafetyNetPollInterval => options.Value.SafetyNetPollInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.DispatcherEnabled)
        {
            MessagingLog.DispatcherDisabled(logger);
            return;
        }

        try
        {
            await SweepAsync(stoppingToken).ConfigureAwait(false);

            while (!stoppingToken.IsCancellationRequested)
            {
                await WaitForWorkAsync(stoppingToken).ConfigureAwait(false);

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _scheduledWake?.Dispose();
            _scheduledWake = null;
        }
    }

    /// <summary>Deliver, purge, schedule. A failure in any step leaves the schedule unreliable (and with a long poll
    /// interval nothing else would wake the dispatcher), so a failed sweep schedules a short retry wake with a
    /// growing backoff, capped at the poll interval.</summary>
    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        var ok = await RunGuardedAsync(DeliverAllAsync, stoppingToken).ConfigureAwait(false);
        ok &= await RunGuardedAsync(async ct => await purger.RunIfDueAsync(ct).ConfigureAwait(false), stoppingToken).ConfigureAwait(false);
        ok &= await RunGuardedAsync(ScheduleNextWakeAsync, stoppingToken).ConfigureAwait(false);

        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        if (ok)
        {
            _consecutiveFailedSweeps = 0;
            return;
        }

        var exponent = Math.Min(_consecutiveFailedSweeps, 20);
        _consecutiveFailedSweeps++;
        var retryDelay = TimeSpan.FromTicks(DispatcherTiming.ErrorBackoff.Ticks << exponent);
        if (retryDelay > SafetyNetPollInterval || retryDelay <= TimeSpan.Zero)
        {
            retryDelay = SafetyNetPollInterval;
        }

        MessagingLog.SweepFailed(logger, _consecutiveFailedSweeps, retryDelay);
        StartWake(retryDelay, stoppingToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Background-service resilience boundary: any fault in a cycle is logged and retried; it must never fault ExecuteAsync and stop the host.")]
    private async Task<bool> RunGuardedAsync(Func<CancellationToken, Task> cycle, CancellationToken stoppingToken)
    {
        try
        {
            await cycle(stoppingToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return true;
        }
        catch (Exception ex)
        {
            MessagingLog.CycleFailed(logger, ex);
            return false;
        }
    }

    private async Task WaitForWorkAsync(CancellationToken stoppingToken)
    {
        // The losing delay must not stay alive (up to the full poll interval) after a channel wake.
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var channelWait = wakeUp.Reader.WaitToReadAsync(waitCts.Token).AsTask();
        var timerWait = Task.Delay(SafetyNetPollInterval, waitCts.Token);
        await Task.WhenAny(channelWait, timerWait).ConfigureAwait(false);
        await waitCts.CancelAsync().ConfigureAwait(false);

        // Drain queued signals now: the upcoming full sweep covers whatever they were signaling.
        while (wakeUp.Reader.TryRead(out _))
        {
            // Discard.
        }
    }

    private async Task DeliverAllAsync(CancellationToken stoppingToken)
    {
        foreach (var outbox in registry.GetOutboxNames())
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // One row per claim, so a slow delivery never keeps other rows' leases ticking.
                var message = await ClaimNextAsync(outbox, stoppingToken).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                await DeliverAsync(outbox, message, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Atomically claims the oldest due row: Pending and due, or Delivering with an expired lease (its
    /// dispatcher crashed; that counts as a failed attempt). The row lock is held only for this short
    /// transaction; afterwards the Delivering status plus the lease protect the row.</summary>
    private async Task<OutboxMessage?> ClaimNextAsync(string outbox, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            var sql = SqlFor(db, outbox);

            var now = time.GetUtcNow();
            var transaction = await db.Database.BeginTransactionAsync(stoppingToken).ConfigureAwait(false);
            await using var transactionScope = transaction.ConfigureAwait(false);
            var message = (await db.Set<OutboxMessage>(MessagingNames.Outbox(outbox))
                .FromSql(SqlText.Sql(
                    $$"""
                    SELECT * FROM {{sql.Table}}
                    WHERE ({{sql.Status}} = {0} AND ({{sql.NextAttemptAt}} IS NULL OR {{sql.NextAttemptAt}} <= {1}))
                       OR ({{sql.Status}} = {2} AND ({{sql.LeaseExpiresAt}} IS NULL OR {{sql.LeaseExpiresAt}} < {1}))
                    ORDER BY {{sql.OccurredAt}}, {{sql.Id}}
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """,
                    nameof(OutboxStatus.Pending), now, nameof(OutboxStatus.Delivering)))
                .ToListAsync(stoppingToken).ConfigureAwait(false)).SingleOrDefault();

            if (message is null)
            {
                return null;
            }

            if (message.Status == OutboxStatus.Delivering)
            {
                message.Attempts++;
                message.LastError = "Interrupted: lease expired while Delivering.";
                if (!RetryPolicy.ShouldRetry(message.Attempts, options.Value.MaxAttempts))
                {
                    message.Status = OutboxStatus.Dead;
                    message.LeaseOwner = null;
                    message.LeaseExpiresAt = null;
                    message.NextAttemptAt = null;
                    await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
                    await transaction.CommitAsync(stoppingToken).ConfigureAwait(false);
                    MessagingLog.EventDead(logger, message.Type, message.Id, message.Attempts, "unknown (lease expired)");
                    continue;
                }
            }

            message.Status = OutboxStatus.Delivering;
            message.LeaseOwner = _workerId;
            message.LeaseExpiresAt = now + LeaseDuration;
            message.NextAttemptAt = null;
            await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);
            await transaction.CommitAsync(stoppingToken).ConfigureAwait(false);
            return message;
        }

        return null;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Any transport failure is a failed delivery attempt: it is recorded on the row, then retried or marked Dead.")]
    private async Task DeliverAsync(string outbox, OutboxMessage message, CancellationToken stoppingToken)
    {
        var envelope = new IntegrationEventEnvelope(message.Id, message.Type, message.Version, message.OccurredAt, message.PayloadJson);

        using var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var renewal = RenewLeaseAsync(outbox, message.Id, renewalCts.Token);

        try
        {
            await transport.SendAsync(envelope, stoppingToken).ConfigureAwait(false);
            await StopRenewalAsync(renewalCts, renewal).ConfigureAwait(false);

            if (await TryMarkDeliveredAsync(outbox, message).ConfigureAwait(false))
            {
                MessagingLog.Delivered(logger, message.Type, message.Id);
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            await StopRenewalAsync(renewalCts, renewal).ConfigureAwait(false);
            await RecordFailureAsync(outbox, message, ex).ConfigureAwait(false);
        }
        finally
        {
            await StopRenewalAsync(renewalCts, renewal).ConfigureAwait(false);
        }
    }

    private static async Task StopRenewalAsync(CancellationTokenSource renewalCts, Task renewal)
    {
        await renewalCts.CancelAsync().ConfigureAwait(false);
        await renewal.ConfigureAwait(false);
    }

    /// <summary>Extends the lease at a third of its duration until cancelled. Failures are logged and retried on the
    /// next tick; a lost lease just stops renewing, since the outcome write is ownership-checked.</summary>
    private async Task RenewLeaseAsync(string outbox, Guid eventId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(LeaseDuration / 3);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<TContext>();
                    var sql = SqlFor(db, outbox);
                    var rows = await db.Database.ExecuteSqlAsync(
                        SqlText.Sql(
                            $$"""
                            UPDATE {{sql.Table}} SET {{sql.LeaseExpiresAt}} = {0}
                            WHERE {{sql.Id}} = {1} AND {{sql.LeaseOwner}} = {2} AND {{sql.Status}} = {3}
                            """,
                            time.GetUtcNow() + LeaseDuration, eventId, _workerId, nameof(OutboxStatus.Delivering)),
                        cancellationToken).ConfigureAwait(false);
                    if (rows == 0)
                    {
                        MessagingLog.LeaseLost(logger, eventId);
                        return;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    MessagingLog.LeaseRenewalFailed(logger, ex, eventId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Delivery finished or the host is shutting down.
        }
    }

    /// <summary>Non-cancellable: a delivery that already succeeded must have its outcome recorded even if
    /// shutdown fires in this instant (a lost mark only means one more, harmless, redelivery).</summary>
    private async Task<bool> TryMarkDeliveredAsync(string outbox, OutboxMessage message)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var sql = SqlFor(db, outbox);

        var rows = await db.Database.ExecuteSqlAsync(
            SqlText.Sql(
                $$"""
                UPDATE {{sql.Table}}
                SET {{sql.Status}} = {0}, {{sql.NextAttemptAt}} = NULL, {{sql.LeaseOwner}} = NULL, {{sql.LeaseExpiresAt}} = NULL
                WHERE {{sql.Id}} = {1} AND {{sql.LeaseOwner}} = {2}
                """,
                nameof(OutboxStatus.Delivered), message.Id, _workerId),
            CancellationToken.None).ConfigureAwait(false);

        if (rows == 0)
        {
            MessagingLog.OutcomeDiscarded(logger, message.Type, message.Id);
        }

        return rows > 0;
    }

    private async Task RecordFailureAsync(string outbox, OutboxMessage message, Exception exception)
    {
        var delivery = exception as IntegrationEventDeliveryException;
        var attempts = message.Attempts + 1;
        var retry = delivery is not { Permanent: true } && RetryPolicy.ShouldRetry(attempts, options.Value.MaxAttempts);
        var error = exception.Message.Length > OutboxMessage.LastErrorMaxLength
            ? exception.Message[..OutboxMessage.LastErrorMaxLength]
            : exception.Message;
        var subscribers = delivery is { FailedHandlers.Count: > 0 } ? string.Join(", ", delivery.FailedHandlers) : "n/a";

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var sql = SqlFor(db, outbox);
        var now = time.GetUtcNow();

        int rows;
        if (retry)
        {
            var next = now + RetryPolicy.BackoffDelay(attempts, options.Value.MaxBackoff);
            rows = await db.Database.ExecuteSqlAsync(
                SqlText.Sql(
                    $$"""
                    UPDATE {{sql.Table}}
                    SET {{sql.Status}} = {0}, {{sql.Attempts}} = {1}, {{sql.LastError}} = {2}, {{sql.NextAttemptAt}} = {3},
                        {{sql.LeaseOwner}} = NULL, {{sql.LeaseExpiresAt}} = NULL
                    WHERE {{sql.Id}} = {4} AND {{sql.LeaseOwner}} = {5}
                    """,
                    nameof(OutboxStatus.Pending), attempts, error, next, message.Id, _workerId),
                CancellationToken.None).ConfigureAwait(false);
            if (rows > 0)
            {
                MessagingLog.DeliveryFailedWillRetry(logger, message.Type, message.Id, attempts, next, subscribers);
            }
        }
        else
        {
            rows = await db.Database.ExecuteSqlAsync(
                SqlText.Sql(
                    $$"""
                    UPDATE {{sql.Table}}
                    SET {{sql.Status}} = {0}, {{sql.Attempts}} = {1}, {{sql.LastError}} = {2}, {{sql.NextAttemptAt}} = NULL,
                        {{sql.LeaseOwner}} = NULL, {{sql.LeaseExpiresAt}} = NULL
                    WHERE {{sql.Id}} = {3} AND {{sql.LeaseOwner}} = {4}
                    """,
                    nameof(OutboxStatus.Dead), attempts, error, message.Id, _workerId),
                CancellationToken.None).ConfigureAwait(false);
            if (rows > 0)
            {
                MessagingLog.EventDead(logger, message.Type, message.Id, attempts, subscribers);
            }
        }

        if (rows == 0)
        {
            MessagingLog.OutcomeDiscarded(logger, message.Type, message.Id);
        }
    }

    /// <summary>End of every sweep: one cheap query per outbox for the earliest lease expiry among Delivering rows
    /// and the earliest NextAttemptAt among Pending rows, then a single scheduled wake at
    /// min(that + margin, safety-net poll). Replaces the previous scheduled wake, so at most one is pending.</summary>
    private async Task ScheduleNextWakeAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        DateTimeOffset? earliest = null;
        foreach (var outbox in registry.GetOutboxNames())
        {
            var sql = SqlFor(db, outbox);
            var next = await db.Database
                .SqlQuery<DateTimeOffset?>(SqlText.Sql(
                    $$"""
                    SELECT LEAST(
                        (SELECT MIN({{sql.LeaseExpiresAt}}) FROM {{sql.Table}} WHERE {{sql.Status}} = {0}),
                        (SELECT MIN({{sql.NextAttemptAt}}) FROM {{sql.Table}} WHERE {{sql.Status}} = {1})) AS "Value"
                    """,
                    nameof(OutboxStatus.Delivering), nameof(OutboxStatus.Pending)))
                .SingleAsync(stoppingToken).ConfigureAwait(false);
            if (next is { } value && (earliest is null || value < earliest))
            {
                earliest = value;
            }
        }

        if (earliest is null)
        {
            ReplaceWake(null);
            return;
        }

        var delay = earliest.Value - time.GetUtcNow() + DispatcherTiming.WakeMargin;
        if (delay < DispatcherTiming.WakeMargin)
        {
            delay = DispatcherTiming.WakeMargin;
        }

        if (delay > SafetyNetPollInterval)
        {
            delay = SafetyNetPollInterval;
        }

        StartWake(delay, stoppingToken);
    }

    private OutboxSql SqlFor(DbContext db, string outbox)
    {
        if (!_sql.TryGetValue(outbox, out var sql))
        {
            sql = OutboxSql.From(db, outbox);
            _sql[outbox] = sql;
        }

        return sql;
    }

    private void ReplaceWake(CancellationTokenSource? next)
    {
        var previous = _scheduledWake;
        _scheduledWake = next;
        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }
    }

    /// <summary>Fire-and-forget wake after <paramref name="delay"/>; replaces any previous one.</summary>
    private void StartWake(TimeSpan delay, CancellationToken stoppingToken)
    {
        var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var token = wake.Token;
        ReplaceWake(wake);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
                wakeUp.Signal();
            }
            catch (OperationCanceledException)
            {
                // Replaced by a newer schedule, or the host is shutting down.
            }
        }, CancellationToken.None);
    }
}

internal static class DispatcherTiming
{
    /// <summary>Slack added to a scheduled wake so the row it targets is already due when the sweep runs.</summary>
    public static readonly TimeSpan WakeMargin = TimeSpan.FromMilliseconds(100);

    /// <summary>First retry delay after a failed sweep; doubles per consecutive failure.</summary>
    public static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(5);
}
