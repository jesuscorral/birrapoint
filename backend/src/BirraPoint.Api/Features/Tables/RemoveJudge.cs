using System.Security.Cryptography;
using System.Text;
using BirraPoint.Api.Common.Audit;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Evaluations;
using BirraPoint.Api.Realtime;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Tables;

/// <summary>DELETE /competitions/{id}/tables/{tableId}/judges/{judgeId} (contracts/rest-api.md
/// §Tables, T086, FR-039): live removal of a judge from an actively-assigned table. Returns null
/// when the competition isn't owned by the caller, the table doesn't belong to it, or the judge
/// has no active (non-removed) TableJudge row at that table — the endpoint maps all three to a
/// plain 404, never leaking existence (same convention as CreateTable/CorrectEvaluation). Throws
/// a 409 invalid-state-transition when the competition isn't InEvaluation (FR-039 scopes this to
/// "the live event"; same convention as UpdateTable.cs's Draft/Active gate). Also closes a
/// data-integrity hole UpdateTable.cs would otherwise hit: TableAssignmentApplier filters on
/// RemovedAt == null to compute who to re-add, so a soft-removed judge re-added via a Draft/Active
/// PUT would collide with the still-tracked row on TableJudge's composite PK — but UpdateTable
/// already refuses writes once the competition reaches InEvaluation, so gating removal to that
/// same state means the two can never overlap. No new guard is added to the judge-workspace
/// slices: JudgeTableAccess.FindActiveMembershipAsync (already filtering on RemovedAt == null and
/// reused by every one of them) starts rejecting the removed judge the instant this handler sets
/// RemovedAt. Already-submitted evaluations are left untouched — TableJudge rows are never
/// hard-deleted once evaluations exist (data-model.md). Once RemovedAt is flushed, every open
/// DiscrepancyAlert at this table is re-reconciled (DiscrepancyReconciler.ReconcileAsync, which
/// excludes removed judges from the involvement math) — otherwise an alert the removed judge was
/// party to could never resolve: the only other paths that call reconciliation (SubmitEvaluation,
/// AdjustEvaluation) both require an active table membership the removed judge no longer has, and
/// CloseTable hard-blocks on any Open alert, so without this the table could never close.</summary>
public sealed record RemoveJudgeCommand(Guid CompetitionId, Guid TableId, Guid JudgeId) : IRequest<RemoveJudgeResult?>;

public sealed record RemoveJudgeResult(Guid TableId, Guid JudgeId);

public sealed class RemoveJudgeCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, IAuditWriter auditWriter, IEventPublisher eventPublisher)
    : IRequestHandler<RemoveJudgeCommand, RemoveJudgeResult?>
{
    public async Task<RemoveJudgeResult?> Handle(RemoveJudgeCommand request, CancellationToken cancellationToken)
    {
        var competitionState = await dbContext.Competitions
            .Where(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub)
            .Select(c => (CompetitionState?)c.State)
            .SingleOrDefaultAsync(cancellationToken);
        if (competitionState is null)
        {
            return null;
        }

        // Scoped through TastingTable.CompetitionId (TableJudge has no CompetitionId of its own) —
        // this also prevents a table/judge id from a different competition being addressed via a
        // route that happens to name a competition the caller does own. AsNoTracking: this row is
        // only used for the pre-transaction existence check below — leaving it untracked means the
        // FOR UPDATE re-fetch under the transaction is this DbContext's first tracked instance of
        // the row, so its RemovedAt reflects the true post-lock state rather than EF's identity
        // resolution silently handing back this (potentially stale) pre-lock instance.
        var tableJudgeExists = await dbContext.TableJudges
            .AsNoTracking()
            .Where(tj => tj.TastingTableId == request.TableId && tj.JudgeId == request.JudgeId && tj.RemovedAt == null)
            .Join(dbContext.TastingTables, tj => tj.TastingTableId, t => t.Id, (tj, t) => new { t.CompetitionId })
            .AnyAsync(x => x.CompetitionId == request.CompetitionId, cancellationToken);

        if (!tableJudgeExists)
        {
            return null;
        }

        // FR-039 scopes live removal to "the live event" — same Draft/Active-only gate shape as
        // UpdateTable.cs/CreateTable.cs, just inverted to the one state this endpoint allows.
        if (competitionState != CompetitionState.InEvaluation)
        {
            throw new DomainException(
                DomainErrorType.InvalidStateTransition,
                "Judges can only be removed from a table while the competition is in InEvaluation state.");
        }

        var judgeDisplayName = await dbContext.Judges
            .Where(j => j.Id == request.JudgeId)
            .Select(j => j.DisplayName)
            .SingleAsync(cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Row lock to serialize concurrent removals of the same (table, judge) pair — two DELETE
        // requests racing the same instant must not both read RemovedAt == null and both commit a
        // removal (which would also double-write the audit log and double-emit JudgeRemoved). Same
        // pattern as Features/Evaluations/CloseTable.cs's one-shot table-state flip; TableJudge's
        // composite (TastingTableId, JudgeId) key stands in for CloseTable's single Id column.
        var lockedTableJudge = await dbContext.TableJudges
            .FromSqlInterpolated(
                $"SELECT * FROM \"TableJudges\" WHERE \"TastingTableId\" = {request.TableId} AND \"JudgeId\" = {request.JudgeId} FOR UPDATE")
            .SingleAsync(cancellationToken);

        if (lockedTableJudge.RemovedAt is not null)
        {
            return null;
        }

        lockedTableJudge.RemovedAt = DateTimeOffset.UtcNow;

        var entityId = RemoveJudgeRules.ComputeAuditEntityId(request.TableId, request.JudgeId);
        auditWriter.Record(
            "JudgeRemoved",
            nameof(TableJudge),
            entityId,
            before: new { tableId = request.TableId, judgeId = request.JudgeId, removedAt = (DateTimeOffset?)null },
            after: new { tableId = request.TableId, judgeId = request.JudgeId, removedAt = lockedTableJudge.RemovedAt });

        // Flush the removal (and the audit row above) BEFORE reconciling — DiscrepancyReconciler.
        // ReconcileAsync's removed-judge exclusion runs a fresh DB query, not a change-tracker
        // lookup, so it can only see this judge as removed once RemovedAt has actually reached the
        // database (still inside this transaction, so nothing is visible to anyone else yet).
        await dbContext.SaveChangesAsync(cancellationToken);

        // Re-derive every open discrepancy at this table now that this judge's evaluations no
        // longer count toward involvement: one they were party to may now resolve if the remaining
        // judges converge; one they weren't involved in is untouched; one where the remaining judges
        // still diverge stays open (FR-039 — see this handler's own doc comment for why this is
        // necessary at all). Collected here, inside the transaction, so the events below only fire
        // for alerts that are actually committed as resolved.
        var openAlertEntryIds = await dbContext.DiscrepancyAlerts
            .Where(a => a.TastingTableId == request.TableId && a.Status == DiscrepancyStatus.Open)
            .Select(a => a.BeerEntryId)
            .ToListAsync(cancellationToken);

        var resolvedAlerts = new List<(Guid AlertId, Guid BeerEntryId)>();
        foreach (var beerEntryId in openAlertEntryIds)
        {
            var outcome = await DiscrepancyReconciler.ReconcileAndSaveAsync(
                dbContext, request.TableId, beerEntryId, cancellationToken);
            if (outcome.AlertResolved)
            {
                resolvedAlerts.Add((outcome.AlertId!.Value, beerEntryId));
            }
        }

        await transaction.CommitAsync(cancellationToken);

        // Emitted only after the transaction above commits (contracts/signalr-hub.md §Delivery
        // semantics). Two separate publishes with different payload shapes per audience
        // (contracts/signalr-hub.md's JudgeRemoved rows), same pattern as CloseTable.cs's
        // two-audience TableClosed emit: the removed judge's own client ejects on the table group;
        // the organizer group also gets the judge's display name (confirmation echo for the dashboard).
        await eventPublisher.PublishToTableAsync(
            request.TableId, CompetitionEvents.JudgeRemoved, new { tableId = request.TableId, judgeId = request.JudgeId }, CancellationToken.None);

        await eventPublisher.PublishToOrganizersAsync(
            request.CompetitionId,
            CompetitionEvents.JudgeRemoved,
            new { tableId = request.TableId, judgeId = request.JudgeId, judgeDisplayName },
            CancellationToken.None);

        // One DiscrepancyResolved pair per alert this removal resolved — same wire shape and
        // dual-audience routing as AdjustEvaluation.cs's PublishDiscrepancyEventAsync
        // (contracts/signalr-hub.md's DiscrepancyResolved rows).
        foreach (var (alertId, beerEntryId) in resolvedAlerts)
        {
            var blindCode = await dbContext.BeerEntries
                .Where(be => be.Id == beerEntryId)
                .Select(be => be.BlindCode)
                .SingleAsync(cancellationToken);

            var payload = new { alertId, tableId = request.TableId, blindCode };

            await eventPublisher.PublishToTableAsync(
                request.TableId, CompetitionEvents.DiscrepancyResolved, payload, CancellationToken.None);

            await eventPublisher.PublishToOrganizersAsync(
                request.CompetitionId, CompetitionEvents.DiscrepancyResolved, payload, CancellationToken.None);
        }

        return new RemoveJudgeResult(request.TableId, request.JudgeId);
    }
}

/// <summary>Pure helper for RemoveJudgeCommandHandler — no EF/DB dependency, so it's unit-testable
/// directly (same "pure rule class beside the DB-touching handler" pattern as
/// Features/Evaluations/CloseTableRules and Features/Evaluations/SubmitEvaluationRules).</summary>
public static class RemoveJudgeRules
{
    /// <summary>Composite (TastingTableId, JudgeId) PK has no single Guid id to key the audit row
    /// on, and AuditLog.EntityId is capped at 50 chars (AuditLogConfiguration.cs) — too short for
    /// both GUIDs verbatim, so a SHA-256 hash of "{tableId}:{judgeId}" (same hex-digest idiom as
    /// BjcpStyleCatalogLoader), truncated to 40 hex chars (160 bits) to stay well within the cap,
    /// gives a deterministic, effectively-unique key for this pair.</summary>
    public static string ComputeAuditEntityId(Guid tableId, Guid judgeId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tableId}:{judgeId}")))[..40];
}
