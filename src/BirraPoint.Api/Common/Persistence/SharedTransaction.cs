using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BirraPoint.Api.Common.Persistence;

/// <summary>
/// Per-scope state of the unit of work shared by every <see cref="BirraPointDbContext"/> of one DI scope (FR-096,
/// ADR-0043): the contexts created in the scope, the database transaction currently open on the scope's single
/// connection, and the contexts that joined it. Not thread-safe, like the scope itself.
/// </summary>
internal sealed class SharedTransaction
{
    private readonly List<DbContext> _contexts = [];
    private readonly Dictionary<DbContext, IDbContextTransaction> _enlisted = [];
    private DbTransaction? _current;

    /// <summary>True while the unit of work saves the other contexts; their interceptors must not start another
    /// round.</summary>
    public bool SavingParticipants { get; set; }

    /// <summary>The transaction the unit of work started itself (to be committed or rolled back by it), if any.</summary>
    public IDbContextTransaction? OwnedByUnitOfWork { get; set; }

    /// <summary>The transaction open on the scope's connection, whichever context began it.</summary>
    public DbTransaction? Current
    {
        get
        {
            // A transaction disposed without commit or rollback raises no interceptor event; its connection link is gone.
            if (_current is { } stale && IsFinished(stale))
            {
                Ended(stale);
            }

            return _current;
        }
    }

    /// <summary>A completed or disposed transaction has no connection (Npgsql throws once it is disposed).</summary>
    private static bool IsFinished(DbTransaction transaction)
    {
        try
        {
            return transaction.Connection is null;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    public void Register(DbContext context) => _contexts.Add(context);

    /// <summary>Contexts of the scope other than <paramref name="except"/> that hold unsaved changes.</summary>
    public List<DbContext> ParticipantsWithChanges(DbContext except) =>
        [.. _contexts.Where(c => !ReferenceEquals(c, except) && c.ChangeTracker.HasChanges())];

    public void Started(DbTransaction transaction)
    {
        if (Current is null)
        {
            _current = transaction;
        }
    }

    public void Ended(DbTransaction transaction)
    {
        if (!ReferenceEquals(_current, transaction))
        {
            return;
        }

        _current = null;
        ReleaseEnlisted();
    }

    /// <summary>Makes <paramref name="context"/> use the open transaction. A context that already has one (its own,
    /// or one it joined) is left alone. Without an open transaction nothing happens.</summary>
    public void Enlist(DbContext context)
    {
        var current = Current;
        if (current is null || context.Database.CurrentTransaction is not null)
        {
            return;
        }

        _enlisted[context] = context.Database.UseTransaction(current)
            ?? throw new InvalidOperationException("The context did not return the transaction it joined.");
    }

    /// <summary>Detaches the contexts that joined the transaction that just ended, so their next save starts clean.
    /// Disposing a joined (not owned) transaction only clears the context's reference; the database transaction
    /// itself was already completed by its owner.</summary>
    private void ReleaseEnlisted()
    {
        foreach (var transaction in _enlisted.Values)
        {
            transaction.Dispose();
        }

        _enlisted.Clear();
    }
}
