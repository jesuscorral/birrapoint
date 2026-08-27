using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Tables;

/// <summary>Feeds T048's "Unassigned" source column — every entry in the competition plus its
/// current table assignment, null when unassigned.</summary>
public sealed record EntryDto(
    Guid Id,
    string BlindCode,
    string StyleCode,
    string StyleName,
    decimal? AbvLow,
    decimal? AbvHigh,
    decimal AbvPercent,
    string? BeerName,
    bool NotValidForBos,
    Guid? TastingTableId,
    string? TastingTableName,
    // T124: the organizer-defined competition category (wizard step 3) this entry was imported
    // under — null for entries seeded outside the Import slice, which never get one assigned.
    string? CompetitionCategoryName,
    // T124: the BJCP taxonomy's own category (e.g. "21"/"IPA"), a completely independent axis
    // from CompetitionCategoryName. Nullable defensively only: BeerEntry.StyleCode is a required,
    // non-nullable FK to BjcpStyles.Code with OnDelete(Restrict), and BjcpStyle.CategoryNumber /
    // .CategoryName are both `required string`, so an entry whose style has no catalog row cannot
    // exist. Treat null as unreachable rather than a state clients must handle.
    string? BjcpCategoryNumber,
    string? BjcpCategoryName);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record ListEntriesQuery(Guid CompetitionId) : IRequest<IReadOnlyList<EntryDto>?>;

public sealed class ListEntriesQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<ListEntriesQuery, IReadOnlyList<EntryDto>?>
{
    public async Task<IReadOnlyList<EntryDto>?> Handle(ListEntriesQuery request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (!competitionExists)
        {
            return null;
        }

        var entries = await dbContext.BeerEntries
            .Where(e => e.CompetitionId == request.CompetitionId)
            .Select(e => new
            {
                e.Id,
                e.BlindCode,
                e.StyleCode,
                e.AbvPercent,
                e.BeerName,
                e.NotValidForBos,
                e.CompetitionCategoryId,
            })
            .ToListAsync(cancellationToken);

        var styleCodes = entries.Select(e => e.StyleCode).Distinct().ToList();
        var styleByCode = await dbContext.BjcpStyles
            .Where(s => styleCodes.Contains(s.Code))
            .Select(s => new { s.Code, s.Name, s.ABVLow, s.ABVHigh, s.CategoryNumber, s.CategoryName })
            .ToDictionaryAsync(s => s.Code, cancellationToken);

        // T124: the organizer-defined competition category (wizard step 3) is a separate axis from
        // the BJCP taxonomy category carried on the style row — the organizer's own grouping is
        // what they assign beers to tables by, so both travel on the entry.
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

        var entryIds = entries.Select(e => e.Id).ToList();
        var tableByEntryId = await dbContext.TableSamples
            .Where(ts => entryIds.Contains(ts.BeerEntryId))
            .Join(dbContext.TastingTables, ts => ts.TastingTableId, t => t.Id, (ts, t) => new { ts.BeerEntryId, TableId = t.Id, TableName = t.Name })
            .ToDictionaryAsync(x => x.BeerEntryId, cancellationToken);

        return entries
            .Select(e =>
            {
                styleByCode.TryGetValue(e.StyleCode, out var style);
                tableByEntryId.TryGetValue(e.Id, out var table);
                var competitionCategoryName = e.CompetitionCategoryId.HasValue
                    && categoryNameById.TryGetValue(e.CompetitionCategoryId.Value, out var name)
                        ? name
                        : null;
                return new EntryDto(
                    e.Id,
                    e.BlindCode,
                    e.StyleCode,
                    style?.Name ?? e.StyleCode,
                    style?.ABVLow,
                    style?.ABVHigh,
                    e.AbvPercent,
                    e.BeerName,
                    e.NotValidForBos,
                    table?.TableId,
                    table?.TableName,
                    competitionCategoryName,
                    style?.CategoryNumber,
                    style?.CategoryName);
            })
            .OrderBy(e => e.BlindCode)
            .ToList();
    }
}
