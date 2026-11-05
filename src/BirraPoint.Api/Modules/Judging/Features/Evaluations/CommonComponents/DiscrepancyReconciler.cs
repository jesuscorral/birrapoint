using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Judging.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents;

/// <summary>
/// Re-derives which judges are involved for one (table, sample), stages the resulting
/// Evaluation.Status / DiscrepancyAlert open-or-resolve transitions, and saves — retrying once if
/// a concurrent reconciliation for the same pair wins the race to insert the DiscrepancyAlert
/// (DiscrepancyAlertConfiguration's partial unique index on (TastingTableId, BeerEntryId, Status =
/// Open)). Callers (SubmitEvaluation, AdjustEvaluation) call this AFTER their own scores mutation
/// has already been saved — this method owns its own SaveChangesAsync round trip(s), unlike
/// Features/CorrectEvaluation/CorrectEvaluationCommand.cs's audit write, which only stages and lets the caller's save pick it up.
/// </summary>
internal static class DiscrepancyReconciler
{
    internal sealed record DiscrepancyReconciliationOutcome(
        IReadOnlyCollection<Guid> InvolvedJudgeIds, Guid? AlertId, bool AlertOpened, bool AlertResolved);

    /// <summary>
    /// This is the analogous race to Evaluation's own (JudgeId, BeerEntryId) idempotency backstop
    /// (Features/SubmitEvaluation/SubmitEvaluationCommand.cs's IsUniqueViolation catch around its insert) — two judges submitting
    /// divergent totals for the same (table, entry) near-simultaneously can both see "no Open alert
    /// yet" and both try to insert one. Unlike that simpler case, this SaveChangesAsync batch also
    /// stages both evaluations' Status flips in the same transaction as the alert insert: when the
    /// insert fails, the WHOLE batch rolls back server-side, but the in-memory Status mutations —
    /// plain C# property sets, not SQL — are still sitting on the tracked entities, now describing a
    /// state that was never persisted. Recovery can't just detach the failed alert and move on: it
    /// must discard every entity this attempt staged (<see cref="DiscardStagedChangesAsync"/>) and
    /// re-derive from what the winning transaction actually committed before retrying — the retry's
    /// own reconciliation only ever UPDATEs the now-existing Open alert (or nothing), so it cannot
    /// hit the same unique index again.
    /// </summary>
    public static async Task<DiscrepancyReconciliationOutcome> ReconcileAndSaveAsync(
        AppDbContext dbContext, Guid tableId, Guid beerEntryId, CancellationToken cancellationToken)
    {
        var outcome = await ReconcileAsync(dbContext, tableId, beerEntryId, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return outcome;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await DiscardStagedChangesAsync(dbContext, tableId, beerEntryId, cancellationToken);

            var retryOutcome = await ReconcileAsync(dbContext, tableId, beerEntryId, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return retryOutcome;
        }
    }

    /// <summary>Reverts this attempt's speculative Evaluation.Status mutations and discards its
    /// never-persisted DiscrepancyAlert insert — the entities this method's own failed
    /// SaveChangesAsync touched, not a blanket "reload everything" (other unrelated changes on the
    /// same DbContext, if any, must survive). The two need different treatment: ReloadAsync is a
    /// documented no-op for an entity in the Added state (there is no DB row to reload — leaving it
    /// tracked as Added would make the retry's own new DiscrepancyAlert collide with THIS stale one
    /// in the same batch, not with any other transaction), so the alert must be explicitly detached
    /// instead; the Modified evaluations, by contrast, DO have a persisted row to reload — that's
    /// what pulls in whatever the winning transaction actually committed for their Status.</summary>
    private static async Task DiscardStagedChangesAsync(
        AppDbContext dbContext, Guid tableId, Guid beerEntryId, CancellationToken cancellationToken)
    {
        foreach (var entry in dbContext.ChangeTracker.Entries<Evaluation>()
            .Where(e => e.State == EntityState.Modified && e.Entity.TastingTableId == tableId && e.Entity.BeerEntryId == beerEntryId)
            .ToList())
        {
            await entry.ReloadAsync(cancellationToken);
        }

        foreach (var entry in dbContext.ChangeTracker.Entries<DiscrepancyAlert>()
            .Where(a => a.State == EntityState.Added && a.Entity.TastingTableId == tableId && a.Entity.BeerEntryId == beerEntryId)
            .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static async Task<DiscrepancyReconciliationOutcome> ReconcileAsync(
        AppDbContext dbContext, Guid tableId, Guid beerEntryId, CancellationToken cancellationToken)
    {
        // Tracked entities (not a projection) — mutating .Status below must stick when
        // ReconcileAndSaveAsync's own SaveChangesAsync runs. The identity map means a retry's
        // ToListAsync reuses the same tracked instances the prior attempt reloaded, rather than
        // creating duplicates.
        var evaluations = await dbContext.Evaluations
            .Where(e => e.TastingTableId == tableId && e.BeerEntryId == beerEntryId)
            .ToListAsync(cancellationToken);

        // A removed judge's total must never keep an alert open (FR-039): once they're gone, their
        // score no longer represents anyone still tasting at this table, so it can't be allowed to
        // block the remaining judges from converging. LiveJudgeRemoval.cs (UpdateTable live removal) flushes RemovedAt before
        // triggering reconciliation, so this DB-level filter sees the removal that prompted this call.
        var removedJudgeIds = await dbContext.TableJudges
            .Where(tj => tj.TastingTableId == tableId && tj.RemovedAt != null)
            .Select(tj => tj.JudgeId)
            .ToListAsync(cancellationToken);
        var removedJudgeIdSet = removedJudgeIds.ToHashSet();

        var totalsByJudge = evaluations
            .Where(e => !removedJudgeIdSet.Contains(e.JudgeId))
            .ToDictionary(e => e.JudgeId, e => e.Total);
        var involved = DiscrepancyRules.ComputeInvolvedJudgeIds(totalsByJudge);

        foreach (var evaluation in evaluations)
        {
            // A removed judge's evaluation is left untouched, not flipped to Confirmed — reconciling
            // toward consensus is a live-table concept that no longer applies once they've left
            // (same "already-submitted evaluations stay untouched" convention as LiveJudgeRemoval.cs).
            if (removedJudgeIdSet.Contains(evaluation.JudgeId))
            {
                continue;
            }

            evaluation.Status = involved.Contains(evaluation.JudgeId)
                ? EvaluationStatus.PendingConsensus
                : EvaluationStatus.Confirmed;
        }

        var existingAlert = await dbContext.DiscrepancyAlerts
            .SingleOrDefaultAsync(
                a => a.TastingTableId == tableId && a.BeerEntryId == beerEntryId && a.Status == DiscrepancyStatus.Open,
                cancellationToken);

        if (involved.Count > 0)
        {
            if (existingAlert is not null)
            {
                return new DiscrepancyReconciliationOutcome(involved, existingAlert.Id, AlertOpened: false, AlertResolved: false);
            }

            var alert = new DiscrepancyAlert { TastingTableId = tableId, BeerEntryId = beerEntryId };
            dbContext.DiscrepancyAlerts.Add(alert);
            return new DiscrepancyReconciliationOutcome(involved, alert.Id, AlertOpened: true, AlertResolved: false);
        }

        if (existingAlert is not null)
        {
            existingAlert.Status = DiscrepancyStatus.Resolved;
            existingAlert.ResolvedAt = DateTimeOffset.UtcNow;
            return new DiscrepancyReconciliationOutcome(involved, existingAlert.Id, AlertOpened: false, AlertResolved: true);
        }

        return new DiscrepancyReconciliationOutcome(involved, AlertId: null, AlertOpened: false, AlertResolved: false);
    }

    /// <summary>Same detection as Features/SubmitEvaluation/SubmitEvaluationCommand.cs's IsUniqueViolation (Npgsql wraps a
    /// unique-constraint violation in a DbUpdateException whose InnerException is a
    /// PostgresException with SqlState 23505) — duplicated rather than shared since it's a
    /// one-line predicate and the two call sites already duplicate comparable small helpers
    /// (e.g. the FluentValidation score-cap rules).</summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
