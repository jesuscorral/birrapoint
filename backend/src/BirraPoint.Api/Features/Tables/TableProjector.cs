using BirraPoint.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Tables;

/// <summary>Builds the GET/response projection for persisted tables — shared by ListTables and the CreateTable/UpdateTable responses.</summary>
internal static class TableProjector
{
    /// <summary>Single-table projection, for the CreateTable/UpdateTable responses.</summary>
    public static async Task<TableDto> ProjectAsync(AppDbContext dbContext, Guid tableId, CancellationToken cancellationToken)
    {
        var projected = await ProjectManyAsync(dbContext, [tableId], cancellationToken);
        return projected.Single();
    }

    /// <summary>
    /// Projects every requested table in a fixed number of round trips (seven, after AsSplitQuery
    /// below splits the Judges/Samples Include into two — avoids the cartesian-product row blowup a
    /// single query would otherwise materialize), regardless of how many tables there are —
    /// ListTables used to call the single-table path in a loop, which made a 20-table competition
    /// 120 sequential queries against a &lt;200 ms p95 read budget (Principle IX). Returns the
    /// tables in the order their ids were supplied.
    /// </summary>
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

        var styleCodes = entries.Select(e => e.StyleCode).Distinct().ToList();
        var styles = await dbContext.BjcpStyles
            .Where(s => styleCodes.Contains(s.Code))
            .Select(s => new { s.Code, s.Name, s.ABVLow, s.ABVHigh, s.CategoryNumber, s.CategoryName })
            .ToListAsync(cancellationToken);
        var styleByCode = styles.ToDictionary(s => s.Code);

        // T124: the organizer-defined competition category (wizard step 3) is a separate axis from
        // the BJCP taxonomy category carried on the style row — the organizer's own grouping is
        // what they assign beers to tables by, so both travel on the sample.
        var categoryIds = entries.Where(e => e.CompetitionCategoryId.HasValue)
            .Select(e => e.CompetitionCategoryId!.Value).Distinct().ToList();
        var categoryNameById = categoryIds.Count == 0
            ? new Dictionary<Guid, string>()
            // Projected before materialising, so this read does not load (and track) whole
            // CompetitionCategory entities the way the plain ToDictionaryAsync overload does.
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

                // Real submitted ABV (BeerEntry.AbvPercent), not the BJCP style's declared range —
                // the organizer needs actual table balance, and many styles (e.g. historical ones)
                // legitimately have no declared ABV range at all (T122). Rounded to the column's own
                // decimal(4,2) precision to avoid repeating-decimal payload noise (PR #31 review #10).
                var meanAbv = samples.Count > 0
                    ? Math.Round(samples.Average(s => s.AbvPercent), 2)
                    : (decimal?)null;

                // Derived from `samples` (which already falls back to the entry's raw StyleCode via
                // `style?.Name ?? e.StyleCode`), not from the catalog-joined `styles` rows — an entry
                // whose StyleCode has no matching BjcpStyles row must still count here, the same
                // "silently excluded" defect class just fixed for MeanAbv (PR #31 review #7).
                var styleNames = samples.Select(s => s.StyleName).Distinct().OrderBy(name => name).ToList();
                var stats = new TableStatsDto(meanAbv, styleNames.Count, styleNames);

                submittedByTableId.TryGetValue(id, out var submitted);
                var progress = new TableProgressDto(submitted, judges.Count * samples.Count);

                return new TableDto(table.Id, table.Name, table.State, judges, samples, progress, stats);
            })
            .ToList();
    }
}
