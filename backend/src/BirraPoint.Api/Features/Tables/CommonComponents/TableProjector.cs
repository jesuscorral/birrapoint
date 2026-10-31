using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Features.Tables.CommonComponents.Dtos;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Tables.CommonComponents;

/// <summary>Builds the GET/response projection for persisted tables — shared by ListTables and the CreateTable/UpdateTable responses.</summary>
internal static class TableProjector
{
    /// <summary>Single-table projection, for the CreateTable/UpdateTable responses.</summary>
    public static async Task<TableDto> ProjectAsync(AppDbContext dbContext, Guid tableId, CancellationToken cancellationToken)
    {
        var projected = await ProjectManyAsync(dbContext, [tableId], cancellationToken);
        return projected.Single();
    }

    public static async Task<IReadOnlyList<TableDto>> ProjectManyAsync(
        AppDbContext dbContext, IReadOnlyList<Guid> tableIds, CancellationToken cancellationToken)
    {
        if (tableIds.Count == 0)
        {
            return [];
        }

        var tables = await dbContext.TastingTables
            .Where(t => tableIds.Contains(t.Id))
            .Include(t => t.Judges.Where(j => j.RemovedAt == null))
            .Include(t => t.Samples)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var judgeIds = tables.SelectMany(t => t.Judges).Select(j => j.JudgeId).Distinct().ToList();
        var judgeById = (await dbContext.Judges
                .Where(j => judgeIds.Contains(j.Id))
                .Select(j => new TableJudgeDto(j.Id, j.Email, j.DisplayName))
                .ToListAsync(cancellationToken))
            .ToDictionary(j => j.Id);

        var entryIds = tables.SelectMany(t => t.Samples).Select(s => s.BeerEntryId).Distinct().ToList();
        var entries = await dbContext.BeerEntries
            .Where(e => entryIds.Contains(e.Id))
            .Select(e => new
            {
                e.Id,
                e.BlindCode,
                e.StyleCode,
                e.AbvPercent,
                e.NotValidForBos,
                e.EntryInstructions,
                e.CompetitionCategoryId,
            })
            .ToListAsync(cancellationToken);
        var entryById = entries.ToDictionary(e => e.Id);

        var styleCodes = entries.Select(e => e.StyleCode).Distinct(StringComparer.Ordinal).ToList();
        var styles = await dbContext.BjcpStyles
            .Where(s => styleCodes.Contains(s.Code))
            .Select(s => new { s.Code, s.Name, s.ABVLow, s.ABVHigh, s.CategoryNumber, s.CategoryName })
            .ToListAsync(cancellationToken);
        var styleByCode = styles.ToDictionary(s => s.Code, StringComparer.Ordinal);

        var categoryIds = entries.Where(e => e.CompetitionCategoryId.HasValue)
            .Select(e => e.CompetitionCategoryId!.Value).Distinct().ToList();
        var categoryNameById = categoryIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await dbContext.CompetitionCategories
                    .Where(c => categoryIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.Name })
                    .ToListAsync(cancellationToken))
                .ToDictionary(c => c.Id, c => c.Name);

        var submittedByTableId = (await dbContext.Evaluations
                .Where(e => tableIds.Contains(e.TastingTableId))
                .GroupBy(e => e.TastingTableId)
                .Select(g => new { TableId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken))
            .ToDictionary(g => g.TableId, g => g.Count);

        var tableById = tables.ToDictionary(t => t.Id);

        return tableIds
            .Where(tableById.ContainsKey)
            .Select(id =>
            {
                var table = tableById[id];

                var judges = table.Judges
                    .Select(j => judgeById.TryGetValue(j.JudgeId, out var dto) ? dto : null)
                    .Where(dto => dto is not null)
                    .Select(dto => dto!)
                    .ToList();

                var samples = table.Samples
                    .Select(s => entryById.TryGetValue(s.BeerEntryId, out var e) ? e : null)
                    .Where(e => e is not null)
                    .Select(e =>
                    {
                        styleByCode.TryGetValue(e!.StyleCode, out var style);
                        var competitionCategoryName = e.CompetitionCategoryId.HasValue
                            && categoryNameById.TryGetValue(e.CompetitionCategoryId.Value, out var name)
                                ? name
                                : null;
                        return new TableSampleDto(
                            e.Id, e.BlindCode, e.StyleCode, style?.Name ?? e.StyleCode, style?.ABVLow, style?.ABVHigh,
                            e.AbvPercent, e.NotValidForBos, e.EntryInstructions,
                            competitionCategoryName, style?.CategoryNumber, style?.CategoryName);
                    })
                    .ToList();

                var meanAbv = samples.Count > 0
                    ? Math.Round(samples.Average(s => s.AbvPercent), 2)
                    : (decimal?)null;

                var styleNames = samples.Select(s => s.StyleName).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.CurrentCulture).ToList();
                var stats = new TableStatsDto(meanAbv, styleNames.Count, styleNames);

                submittedByTableId.TryGetValue(id, out var submitted);
                var progress = new TableProgressDto(submitted, judges.Count * samples.Count);

                return new TableDto(table.Id, table.Name, table.State, judges, samples, progress, stats);
            })
            .ToList();
    }
}
