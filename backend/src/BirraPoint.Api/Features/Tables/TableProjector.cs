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
        // legitimately have no declared ABV range at all (T122).
        var stats = new TableStatsDto(
            entries.Count > 0 ? entries.Average(e => e.AbvPercent) : null,
            styles.Count,
            styles.Select(s => s.Name).OrderBy(name => name).ToList());

        var submitted = await dbContext.Evaluations.CountAsync(e => e.TastingTableId == table.Id, cancellationToken);
        var progress = new TableProgressDto(submitted, judges.Count * samples.Count);

        return new TableDto(table.Id, table.Name, table.State, judges, samples, progress, stats);
    }
}
