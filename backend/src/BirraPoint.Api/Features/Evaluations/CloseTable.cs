using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.TastingOrder;
using BirraPoint.Api.Realtime;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Evaluations;

/// <summary>Wire shape for FR-042's per-sample consolidated score — matches the organizer-group
/// TableClosed payload in contracts/signalr-hub.md (`{ blindCode, mean }`).</summary>
public sealed record ConsolidatedScoreDto(string BlindCode, decimal Mean);

/// <summary>POST /me/tables/{tableId}/close (contracts/rest-api.md §Judge workspace, T062-T063,
/// FR-033/FR-042). Returns null when the caller is not an active member of this table — the
/// endpoint maps that to a plain 404, same convention as SubmitEvaluationCommand/FixOrderCommand.</summary>
public sealed record CloseTableCommand(Guid TableId) : IRequest<CloseTableResult?>;

public sealed record CloseTableResult(IReadOnlyList<ConsolidatedScoreDto> ConsolidatedScores);

public sealed class CloseTableCommandHandler(AppDbContext dbContext, ICurrentUser currentUser, IEventPublisher eventPublisher)
    : IRequestHandler<CloseTableCommand, CloseTableResult?>
{
    public async Task<CloseTableResult?> Handle(CloseTableCommand request, CancellationToken cancellationToken)
    {
        var judges = await currentUser.GetJudgeRecordsAsync(cancellationToken);
        var judgeId = await JudgeTableAccess.FindActiveMembershipAsync(dbContext, judges, request.TableId, cancellationToken);
        if (judgeId is null)
        {
            return null;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Row lock to serialize concurrent closers — two judges racing POST .../close at the same
        // instant must not both pass the not-already-closed check and both commit Closed (which
        // would also double-emit TableClosed to the organizer group). Same pattern as
        // Features/TastingOrder/FixOrder.cs's one-shot table-state flip. The active TableJudge row
        // found above references an existing TastingTable (FK), so this is always found.
        var table = await dbContext.TastingTables
            .FromSqlInterpolated($"SELECT * FROM \"TastingTables\" WHERE \"Id\" = {request.TableId} FOR UPDATE")
            .SingleAsync(cancellationToken);

        if (table.State == TableState.Closed)
        {
            throw new DomainException(DomainErrorType.TableClosed, "This table is already closed.");
        }

        var activeJudgeIds = await dbContext.TableJudges
            .Where(tj => tj.TastingTableId == request.TableId && tj.RemovedAt == null)
            .Select(tj => tj.JudgeId)
            .ToListAsync(cancellationToken);

        var sampleBeerEntryIds = await dbContext.TableSamples
            .Where(ts => ts.TastingTableId == request.TableId)
            .Select(ts => ts.BeerEntryId)
            .ToListAsync(cancellationToken);

        var blindCodeByEntryId = await dbContext.BeerEntries
            .Where(be => sampleBeerEntryIds.Contains(be.Id))
            .ToDictionaryAsync(be => be.Id, be => be.BlindCode, cancellationToken);

        // Single fetch reused for both the completeness check and the consolidated-mean
        // computation below — the table's evaluations are read-only from this handler's point of
        // view (it only ever mutates TastingTable.State/ClosedAt), so one snapshot suffices.
        var evaluationRows = await dbContext.Evaluations
            .Where(e => e.TastingTableId == request.TableId)
            .Select(e => new { e.JudgeId, e.BeerEntryId, e.Total })
            .ToListAsync(cancellationToken);

        var missingBlindCodes = CloseTableRules.ComputeMissingBlindCodes(
            activeJudgeIds,
            sampleBeerEntryIds,
            evaluationRows.Select(e => (e.JudgeId, e.BeerEntryId)).ToList(),
            blindCodeByEntryId);

        if (missingBlindCodes.Count > 0)
        {
            throw new DomainException(
                DomainErrorType.EvaluationsIncomplete,
                "Not every active judge has submitted every sample at this table.",
                new Dictionary<string, object?> { ["missing"] = missingBlindCodes });
        }

        var openDiscrepancyEntryIds = await dbContext.DiscrepancyAlerts
            .Where(d => d.TastingTableId == request.TableId && d.Status == DiscrepancyStatus.Open)
            .Select(d => d.BeerEntryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (openDiscrepancyEntryIds.Count > 0)
        {
            throw new DomainException(
                DomainErrorType.DiscrepancyOpen,
                "This table has unresolved discrepancy alerts.",
                new Dictionary<string, object?>
                {
                    ["blindCodes"] = openDiscrepancyEntryIds.Select(id => blindCodeByEntryId[id]).ToList(),
                });
        }

        table.State = TableState.Closed;
        table.ClosedAt = DateTimeOffset.UtcNow;

        var consolidatedScores = evaluationRows
            .GroupBy(e => e.BeerEntryId)
            .Select(g => new ConsolidatedScoreDto(
                blindCodeByEntryId[g.Key], CloseTableRules.ComputeMean(g.Select(e => e.Total).ToList())))
            .ToList();

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Emitted only after the transaction above commits (contracts/signalr-hub.md §Delivery
        // semantics). Two separate publishes with different payload shapes per audience
        // (contracts/signalr-hub.md's TableClosed rows): the judge group only learns the table is
        // closed; the organizer group also gets the consolidated means (FR-042).
        await eventPublisher.PublishToTableAsync(
            request.TableId, "TableClosed", new { tableId = request.TableId }, CancellationToken.None);

        await eventPublisher.PublishToOrganizersAsync(
            table.CompetitionId,
            "TableClosed",
            new { tableId = request.TableId, consolidatedScores },
            CancellationToken.None);

        return new CloseTableResult(consolidatedScores);
    }
}
