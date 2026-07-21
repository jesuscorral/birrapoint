using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.TastingOrder;

/// <summary>
/// Shared JudgeSampleDto projection for GetTableSamples and FixOrder's response (data-model.md
/// §Anonymity boundary) — mirrors Features/Tables/TableProjector's dictionary-lookup shape, but
/// selects only BeerEntry.Id/BlindCode/StyleCode into the query, never BeerName or any
/// Participant/EntryCollaborator field (BR-01/FR-019).
/// </summary>
internal static class JudgeSampleProjector
{
    public static async Task<IReadOnlyList<JudgeSampleDto>> ProjectAsync(
        AppDbContext dbContext, Guid tableId, Guid judgeId, CancellationToken cancellationToken)
    {
        var evaluationStatusByEntryId = await dbContext.Evaluations
            .Where(e => e.TastingTableId == tableId && e.JudgeId == judgeId)
            .ToDictionaryAsync(e => e.BeerEntryId, e => e.Status, cancellationToken);

        var samples = await dbContext.TableSamples
            .Where(ts => ts.TastingTableId == tableId)
            .ToListAsync(cancellationToken);

        var entryIds = samples.Select(s => s.BeerEntryId).ToList();
        var entries = await dbContext.BeerEntries
            .Where(e => entryIds.Contains(e.Id))
            .Select(e => new { e.Id, e.BlindCode, e.StyleCode })
            .ToListAsync(cancellationToken);

        var styleCodes = entries.Select(e => e.StyleCode).Distinct().ToList();
        var styleNameByCode = await dbContext.BjcpStyles
            .Where(s => styleCodes.Contains(s.Code))
            .ToDictionaryAsync(s => s.Code, s => s.Name, cancellationToken);

        var sequenceByEntryId = samples.ToDictionary(s => s.BeerEntryId, s => s.SequenceOrder);

        return entries
            .Select(e => new JudgeSampleDto(
                e.Id,
                e.BlindCode,
                e.StyleCode,
                styleNameByCode.GetValueOrDefault(e.StyleCode, e.StyleCode),
                sequenceByEntryId[e.Id],
                MapEvaluationStatus(evaluationStatusByEntryId, e.Id)))
            .OrderBy(dto => dto.SequenceOrder ?? int.MaxValue)
            .ThenBy(dto => dto.BlindCode, StringComparer.Ordinal)
            .ToList();
    }

    private static string MapEvaluationStatus(IReadOnlyDictionary<Guid, EvaluationStatus> statuses, Guid beerEntryId)
    {
        if (!statuses.TryGetValue(beerEntryId, out var status))
        {
            return "NotStarted";
        }

        return status == EvaluationStatus.PendingConsensus ? "PendingConsensus" : "Submitted";
    }
}
