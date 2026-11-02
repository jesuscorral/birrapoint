using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Catalog.Contracts;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Judging.Features.TastingOrder.CommonComponents.Dtos;
using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.TastingOrder.CommonComponents;

/// <summary>
/// Shared JudgeSampleDto projection for GetTableSamples and FixOrder's response (data-model.md
/// §Anonymity boundary) — mirrors Modules/Judging/Features/Tables/TableProjector's dictionary-lookup shape, but
/// selects only BeerEntry.Id/BlindCode/StyleCode into the query, never BeerName or any
/// Participant/EntryCollaborator field (BR-01/FR-019).
/// </summary>
internal static class JudgeSampleProjector
{
    public static async Task<IReadOnlyList<JudgeSampleDto>> ProjectAsync(
        AppDbContext dbContext, IStyleCatalog styleCatalog, Guid tableId, Guid judgeId, CancellationToken cancellationToken)
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
            .Select(e => new { e.Id, e.BlindCode, e.StyleCode, e.AbvPercent, e.EntryInstructions })
            .ToListAsync(cancellationToken);

        var styleCodes = entries.Select(e => e.StyleCode).Distinct(StringComparer.Ordinal).ToList();
        var styleByCode = await styleCatalog.GetByCodesAsync(styleCodes, cancellationToken);

        var sequenceByEntryId = samples.ToDictionary(s => s.BeerEntryId, s => s.SequenceOrder);

        return entries
            .Select(e => new JudgeSampleDto(
                e.Id,
                e.BlindCode,
                e.StyleCode,
                styleByCode.TryGetValue(e.StyleCode, out var style) ? style.Name : e.StyleCode,
                e.AbvPercent,
                sequenceByEntryId[e.Id],
                MapEvaluationStatus(evaluationStatusByEntryId, e.Id),
                e.EntryInstructions))
            .OrderBy(dto => dto.SequenceOrder ?? int.MaxValue)
            .ThenBy(dto => dto.BlindCode, StringComparer.Ordinal)
            .ToList();
    }

    private static string MapEvaluationStatus(Dictionary<Guid, EvaluationStatus> statuses, Guid beerEntryId)
    {
        if (!statuses.TryGetValue(beerEntryId, out var status))
        {
            return "NotStarted";
        }

        return status == EvaluationStatus.PendingConsensus ? "PendingConsensus" : "Submitted";
    }
}
