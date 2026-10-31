using BirraPoint.Api.Common.Audit;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Evaluations;
using BirraPoint.Api.Features.Tables.CommonComponents;
using BirraPoint.Api.Features.Tables.CommonComponents.Dtos;
using BirraPoint.Api.Realtime;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Tables.Features.UpdateTable;

internal static class LiveJudgeRemoval
{
    public static async Task<TableMutationResult> ApplyAsync(
        AppDbContext dbContext,
        IAuditWriter auditWriter,
        IEventPublisher eventPublisher,
        Guid competitionId,
        Guid tableId,
        string name,
        IReadOnlyList<Guid> judgeIds,
        IReadOnlyList<Guid> beerEntryIds,
        CancellationToken cancellationToken)
    {
        var removedJudgeIds = new List<Guid>();
        var resolvedAlerts = new List<(Guid AlertId, Guid BeerEntryId)>();
        var judgeNames = new Dictionary<Guid, string>();

        // Cheap pre-lock rejection with untracked reads; the decision is re-made after the lock
        // (the first tracked instance of each row must be the post-lock one, not an EF
        // identity-resolved stale copy).
        var preLock = await ReadStateAsync(dbContext, tableId, cancellationToken);
        if (LiveEditRules.Classify(preLock.Name, preLock.SampleIds, preLock.ActiveJudgeIds, name, beerEntryIds, judgeIds).Kind
            == LiveEditKind.Forbidden)
        {
            throw Forbidden();
        }

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // Serializes concurrent PUTs on this table (two racing removals must not both read the
            // judge as active and double-write audit/events). Same pattern as CloseTable.
            var lockedTable = await dbContext.TastingTables
                .FromSqlInterpolated($"SELECT * FROM \"TastingTables\" WHERE \"Id\" = {tableId} FOR UPDATE")
                .SingleAsync(cancellationToken);

            if (lockedTable.State == TableState.Closed)
            {
                throw new DomainException(DomainErrorType.TableClosed, "This table is closed and can no longer be edited.");
            }

            var activeRows = await dbContext.TableJudges
                .Where(tj => tj.TastingTableId == tableId && tj.RemovedAt == null)
                .ToListAsync(cancellationToken);
            var sampleIds = await dbContext.TableSamples
                .AsNoTracking()
                .Where(ts => ts.TastingTableId == tableId)
                .Select(ts => ts.BeerEntryId)
                .ToListAsync(cancellationToken);

            var classification = LiveEditRules.Classify(
                lockedTable.Name, sampleIds, activeRows.Select(r => r.JudgeId).ToList(), name, beerEntryIds, judgeIds);

            if (classification.Kind == LiveEditKind.Forbidden)
            {
                throw Forbidden();
            }

            if (classification.Kind == LiveEditKind.Removal)
            {
                removedJudgeIds.AddRange(classification.RemovedJudgeIds);
                judgeNames = await dbContext.Judges
                    .AsNoTracking()
                    .Where(j => removedJudgeIds.Contains(j.Id))
                    .ToDictionaryAsync(j => j.Id, j => j.DisplayName, cancellationToken);

                var removedAt = DateTimeOffset.UtcNow;
                foreach (var row in activeRows.Where(r => removedJudgeIds.Contains(r.JudgeId)))
                {
                    row.RemovedAt = removedAt;
                    auditWriter.Record(
                        "JudgeRemoved",
                        nameof(TableJudge),
                        RemoveJudgeRules.ComputeAuditEntityId(tableId, row.JudgeId),
                        before: new { tableId, judgeId = row.JudgeId, removedAt = (DateTimeOffset?)null },
                        after: new { tableId, judgeId = row.JudgeId, removedAt });
                }

                // Flush BEFORE reconciling: the reconciler's removed-judge exclusion is a fresh DB
                // query, so it only sees RemovedAt once written (still inside this transaction).
                await dbContext.SaveChangesAsync(cancellationToken);

                var openAlertEntryIds = await dbContext.DiscrepancyAlerts
                    .Where(a => a.TastingTableId == tableId && a.Status == DiscrepancyStatus.Open)
                    .Select(a => a.BeerEntryId)
                    .ToListAsync(cancellationToken);

                foreach (var beerEntryId in openAlertEntryIds)
                {
                    var outcome = await DiscrepancyReconciler.ReconcileAndSaveAsync(
                        dbContext, tableId, beerEntryId, cancellationToken);
                    if (outcome.AlertResolved)
                    {
                        resolvedAlerts.Add((outcome.AlertId!.Value, beerEntryId));
                    }
                }

                await transaction.CommitAsync(cancellationToken);
            }
        }

        // Events only after commit (contracts/signalr-hub.md §Delivery semantics).
        foreach (var judgeId in removedJudgeIds)
        {
            await eventPublisher.PublishToTableAsync(
                tableId, CompetitionEvents.JudgeRemoved, new { tableId, judgeId }, CancellationToken.None);

            await eventPublisher.PublishToOrganizersAsync(
                competitionId,
                CompetitionEvents.JudgeRemoved,
                new { tableId, judgeId, judgeDisplayName = judgeNames[judgeId] },
                CancellationToken.None);
        }

        foreach (var (alertId, beerEntryId) in resolvedAlerts)
        {
            var blindCode = await dbContext.BeerEntries
                .AsNoTracking()
                .Where(be => be.Id == beerEntryId)
                .Select(be => be.BlindCode)
                .SingleAsync(cancellationToken);

            var payload = new { alertId, tableId, blindCode };
            await eventPublisher.PublishToTableAsync(tableId, CompetitionEvents.DiscrepancyResolved, payload, CancellationToken.None);
            await eventPublisher.PublishToOrganizersAsync(competitionId, CompetitionEvents.DiscrepancyResolved, payload, CancellationToken.None);
        }

        // Drop tracked TableJudge/TastingTable instances (RemovedAt set above): the projector's
        // filtered Include (RemovedAt == null) would otherwise be merged with them by EF fixup.
        dbContext.ChangeTracker.Clear();

        var dto = await TableProjector.ProjectAsync(dbContext, tableId, cancellationToken);
        return TableMutationResult.From(dto, []);
    }

    private static DomainException Forbidden() => new(
        DomainErrorType.InvalidStateTransition,
        "While the competition is in InEvaluation state a table can only be edited to remove judges; name, samples and added judges cannot change.");

    private static async Task<(string Name, List<Guid> SampleIds, List<Guid> ActiveJudgeIds)> ReadStateAsync(
        AppDbContext dbContext, Guid tableId, CancellationToken cancellationToken)
    {
        var name = await dbContext.TastingTables.AsNoTracking()
            .Where(t => t.Id == tableId).Select(t => t.Name).SingleAsync(cancellationToken);
        var samples = await dbContext.TableSamples.AsNoTracking()
            .Where(ts => ts.TastingTableId == tableId).Select(ts => ts.BeerEntryId).ToListAsync(cancellationToken);
        var judges = await dbContext.TableJudges.AsNoTracking()
            .Where(tj => tj.TastingTableId == tableId && tj.RemovedAt == null).Select(tj => tj.JudgeId).ToListAsync(cancellationToken);
        return (name, samples, judges);
    }
}
