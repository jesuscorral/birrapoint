using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

/// <summary>Notes that a save includes new outbox rows and, when there is no explicit transaction around it,
/// wakes the dispatcher right after the save committed. Inside an explicit transaction the
/// <see cref="OutboxWakeUpTransactionInterceptor"/> signals after the real commit.</summary>
internal sealed class OutboxWakeUpInterceptor(OutboxWakeUp wakeUp) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Note(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Note(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Saved(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Failed(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Failed(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Note(DbContext? context)
    {
        if (context is not null && context.ChangeTracker.Entries<OutboxMessage>().Any(e => e.State == EntityState.Added))
        {
            wakeUp.MarkPending(context);
        }
    }

    private void Saved(DbContext? context)
    {
        if (context is not null && context.Database.CurrentTransaction is null)
        {
            wakeUp.SignalIfPending(context);
        }
    }

    private void Failed(DbContext? context)
    {
        if (context is not null && context.Database.CurrentTransaction is null)
        {
            wakeUp.ClearPending(context);
        }
    }
}

/// <summary>Signals after a transaction that included outbox rows committed; forgets them on rollback.</summary>
internal sealed class OutboxWakeUpTransactionInterceptor(OutboxWakeUp wakeUp) : DbTransactionInterceptor
{
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        Committed(eventData.Context);
        base.TransactionCommitted(transaction, eventData);
    }

    public override Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Committed(eventData.Context);
        return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        RolledBack(eventData.Context);
        base.TransactionRolledBack(transaction, eventData);
    }

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        RolledBack(eventData.Context);
        return base.TransactionRolledBackAsync(transaction, eventData, cancellationToken);
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        RolledBack(eventData.Context);
        base.TransactionFailed(transaction, eventData);
    }

    public override Task TransactionFailedAsync(
        DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        RolledBack(eventData.Context);
        return base.TransactionFailedAsync(transaction, eventData, cancellationToken);
    }

    private void Committed(DbContext? context)
    {
        if (context is not null)
        {
            wakeUp.SignalIfPending(context);
        }
    }

    private void RolledBack(DbContext? context)
    {
        if (context is not null)
        {
            wakeUp.ClearPending(context);
        }
    }
}

public static class MessagingDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Adds the interceptors that wake the dispatcher after a commit that included outbox rows. Call it in the
    /// host's <c>AddDbContext</c> callback: <c>(sp, options) =&gt; options.UseNpgsql(...).AddMessagingInterceptors(sp)</c>.
    /// Without it the dispatcher still delivers, but only on its safety-net poll.
    /// </summary>
    public static DbContextOptionsBuilder AddMessagingInterceptors(this DbContextOptionsBuilder options, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return options.AddInterceptors(
            serviceProvider.GetRequiredService<OutboxWakeUpInterceptor>(),
            serviceProvider.GetRequiredService<OutboxWakeUpTransactionInterceptor>());
    }
}
