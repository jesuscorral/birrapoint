using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BirraPoint.Common.Persistence;

/// <summary>
/// Records the scope's database transaction in <see cref="SharedTransaction"/> when any context begins (or is given)
/// one and clears it when it commits, rolls back or fails, so the other contexts can join it (R-107-2).
/// </summary>
internal sealed class SharedTransactionInterceptor(SharedTransaction shared) : DbTransactionInterceptor
{
    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        shared.Started(result);
        return result;
    }

    public override ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        shared.Started(result);
        return ValueTask.FromResult(result);
    }

    public override DbTransaction TransactionUsed(DbConnection connection, TransactionEventData eventData, DbTransaction result)
    {
        shared.Started(result);
        return result;
    }

    public override ValueTask<DbTransaction> TransactionUsedAsync(
        DbConnection connection, TransactionEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
        shared.Started(result);
        return ValueTask.FromResult(result);
    }

    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        shared.Ended(transaction);
        base.TransactionCommitted(transaction, eventData);
    }

    public override Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        shared.Ended(transaction);
        return base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        shared.Ended(transaction);
        base.TransactionRolledBack(transaction, eventData);
    }

    public override Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        shared.Ended(transaction);
        return base.TransactionRolledBackAsync(transaction, eventData, cancellationToken);
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        shared.Ended(transaction);
        base.TransactionFailed(transaction, eventData);
    }

    public override Task TransactionFailedAsync(
        DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        shared.Ended(transaction);
        return base.TransactionFailedAsync(transaction, eventData, cancellationToken);
    }
}
