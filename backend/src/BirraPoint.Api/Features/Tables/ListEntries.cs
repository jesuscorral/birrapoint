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
    string BeerName,
    bool NotValidForBos,
    Guid? TastingTableId,
    string? TastingTableName);

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
            .Select(e => new { e.Id, e.BlindCode, e.StyleCode, e.BeerName, e.NotValidForBos })
            .ToListAsync(cancellationToken);

        var styleCodes = entries.Select(e => e.StyleCode).Distinct().ToList();
        var styleByCode = await dbContext.BjcpStyles
            .Where(s => styleCodes.Contains(s.Code))
            .Select(s => new { s.Code, s.Name, s.ABVLow, s.ABVHigh })
            .ToDictionaryAsync(s => s.Code, cancellationToken);

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
                return new EntryDto(
                    e.Id,
                    e.BlindCode,
                    e.StyleCode,
                    style?.Name ?? e.StyleCode,
                    style?.ABVLow,
                    style?.ABVHigh,
                    e.BeerName,
                    e.NotValidForBos,
                    table?.TableId,
                    table?.TableName);
            })
            .OrderBy(e => e.BlindCode)
            .ToList();
    }
}
