using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace BirraPoint.Api.Common.Persistence;

/// <summary>
/// Makes one <c>SaveChangesAsync</c> on any context of the scope commit every context's pending changes (staged audit
/// entries, staged jobs, outbox rows of another context) atomically (FR-096, R-107-2):
/// <list type="bullet">
/// <item>Inside a transaction already open on the scope's connection (an explicit one begun on any context, the job
/// worker's, the inbox runner's) the saving context joins it and the other contexts with changes are saved into it
/// after the main save; the transaction's owner commits or rolls back.</item>
/// <item>With no transaction open, and other contexts holding changes, the saving context begins one, the others are
/// saved into it after the main save, and the unit of work commits it (or rolls it back when any save fails).</item>
/// <item>With nothing pending elsewhere EF behaves exactly as without the interceptor.</item>
/// </list>
/// The saves of the other contexts run with <see cref="SharedTransaction.SavingParticipants"/> set, so they do not start
/// another round. No execution strategy that retries is configured on the contexts; one would have to wrap the whole
/// unit of work instead of the individual save.
/// </summary>
internal sealed class UnitOfWorkSaveChangesInterceptor(SharedTransaction shared) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        var context = eventData.Context;
        if (context is not null && !shared.SavingParticipants)
        {
            if (shared.Current is not null)
            {
                shared.Enlist(context);
            }
            else if (shared.OwnedByUnitOfWork is null && shared.ParticipantsWithChanges(context).Count > 0)
            {
                shared.OwnedByUnitOfWork = context.Database.BeginTransaction();
            }
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is not null && !shared.SavingParticipants)
        {
            if (shared.Current is not null)
            {
                shared.Enlist(context);
            }
            else if (shared.OwnedByUnitOfWork is null && shared.ParticipantsWithChanges(context).Count > 0)
            {
                shared.OwnedByUnitOfWork = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        var context = eventData.Context;
        if (context is null || shared.SavingParticipants || shared.Current is null)
        {
            return result;
        }

        var owned = shared.OwnedByUnitOfWork;
        try
        {
            SaveParticipants(context);
            if (owned is not null)
            {
                shared.OwnedByUnitOfWork = null;
                owned.Commit();
                owned.Dispose();
            }
        }
        catch
        {
            RollBack(owned);
            throw;
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null || shared.SavingParticipants || shared.Current is null)
        {
            return result;
        }

        var owned = shared.OwnedByUnitOfWork;
        try
        {
            await SaveParticipantsAsync(context, cancellationToken).ConfigureAwait(false);
            if (owned is not null)
            {
                shared.OwnedByUnitOfWork = null;
                await owned.CommitAsync(cancellationToken).ConfigureAwait(false);
                await owned.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            await RollBackAsync(owned).ConfigureAwait(false);
            throw;
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (!shared.SavingParticipants)
        {
            RollBack(TakeOwned());
        }
    }

    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (!shared.SavingParticipants)
        {
            await RollBackAsync(TakeOwned()).ConfigureAwait(false);
        }
    }

    private IDbContextTransaction? TakeOwned()
    {
        var owned = shared.OwnedByUnitOfWork;
        shared.OwnedByUnitOfWork = null;
        return owned;
    }

    private void SaveParticipants(DbContext main)
    {
        var participants = shared.ParticipantsWithChanges(main);
        if (participants.Count == 0)
        {
            return;
        }

        shared.SavingParticipants = true;
        try
        {
            foreach (var participant in participants)
            {
                shared.Enlist(participant);
                participant.SaveChanges();
            }
        }
        finally
        {
            shared.SavingParticipants = false;
        }
    }

    private async Task SaveParticipantsAsync(DbContext main, CancellationToken cancellationToken)
    {
        var participants = shared.ParticipantsWithChanges(main);
        if (participants.Count == 0)
        {
            return;
        }

        shared.SavingParticipants = true;
        try
        {
            foreach (var participant in participants)
            {
                shared.Enlist(participant);
                await participant.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            shared.SavingParticipants = false;
        }
    }

    private static void RollBack(IDbContextTransaction? owned)
    {
        if (owned is null)
        {
            return;
        }

        owned.Rollback();
        owned.Dispose();
    }

    private static async Task RollBackAsync(IDbContextTransaction? owned)
    {
        if (owned is null)
        {
            return;
        }

        await owned.RollbackAsync().ConfigureAwait(false);
        await owned.DisposeAsync().ConfigureAwait(false);
    }
}
