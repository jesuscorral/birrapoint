using BirraPoint.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Tables;

/// <summary>Builds the GET/response projection for a persisted table — shared by ListTables and the CreateTable/UpdateTable responses.</summary>
internal static class TableProjector
{
    public static async Task<TableDto> ProjectAsync(AppDbContext dbContext, Guid tableId, CancellationToken cancellationToken)
    {
        var table = await dbContext.TastingTables
            .Include(t => t.Judges.Where(j => j.RemovedAt == null))
            .Include(t => t.Samples)
            .FirstAsync(t => t.Id == tableId, cancellationToken);

        var judgeIds = table.Judges.Select(j => j.JudgeId).ToList();
        var judges = await dbContext.Judges
            .Where(j => judgeIds.Contains(j.Id))
            .Select(j => new TableJudgeDto(j.Id, j.Email, j.DisplayName))
            .ToListAsync(cancellationToken);

        var entryIds = table.Samples.Select(s => s.BeerEntryId).ToList();
        var entries = await dbContext.BeerEntries
            .Where(e => entryIds.Contains(e.Id))
            .Select(e => new { e.Id, e.BlindCode, e.StyleCode, e.AbvPercent, e.NotValidForBos, e.EntryInstructions })
            .ToListAsync(cancellationToken);

        var styleCodes = entries.Select(e => e.StyleCode).Distinct().ToList();
        var styles = await dbContext.BjcpStyles
            .Where(s => styleCodes.Contains(s.Code))
            .Select(s => new { s.Code, s.Name, s.ABVLow, s.ABVHigh })
            .ToListAsync(cancellationToken);
        var styleByCode = styles.ToDictionary(s => s.Code);

        var samples = entries
            .Select(e =>
            {
                styleByCode.TryGetValue(e.StyleCode, out var style);
                return new TableSampleDto(
                    e.Id, e.BlindCode, e.StyleCode, style?.Name ?? e.StyleCode, style?.ABVLow, style?.ABVHigh,
                    e.AbvPercent, e.NotValidForBos, e.EntryInstructions);
            })
            .ToList();

        // Real submitted ABV (BeerEntry.AbvPercent), not the BJCP style's declared range —
        // the organizer needs actual table balance, and many styles (e.g. historical ones)
        // legitimately have no declared ABV range at all (T122). Rounded to the column's own
        // decimal(4,2) precision to avoid repeating-decimal payload noise (PR #31 review #10).
        var meanAbv = entries.Count > 0 ? Math.Round(entries.Average(e => e.AbvPercent), 2) : (decimal?)null;

        // Derived from `samples` (which already falls back to the entry's raw StyleCode via
        // `style?.Name ?? e.StyleCode`), not from the catalog-joined `styles` rows — an entry
        // whose StyleCode has no matching BjcpStyles row must still count here, the same
        // "silently excluded" defect class just fixed for MeanAbv (PR #31 review #7).
        var styleNames = samples.Select(s => s.StyleName).Distinct().OrderBy(name => name).ToList();
        var stats = new TableStatsDto(meanAbv, styleNames.Count, styleNames);

        var submitted = await dbContext.Evaluations.CountAsync(e => e.TastingTableId == table.Id, cancellationToken);
        var progress = new TableProgressDto(submitted, judges.Count * samples.Count);

        return new TableDto(table.Id, table.Name, table.State, judges, samples, progress, stats);
    }
}
