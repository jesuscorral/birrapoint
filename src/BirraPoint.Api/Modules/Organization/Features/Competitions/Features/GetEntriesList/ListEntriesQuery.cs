using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Catalog.Contracts;
using BirraPoint.Api.Modules.Organization.Contracts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Competitions.Features.GetEntriesList;

internal sealed record EntryDto(
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
    string? CompetitionCategoryName,
    string? BjcpCategoryNumber,
    string? BjcpCategoryName);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record ListEntriesQuery(Guid CompetitionId) : IRequest<IReadOnlyList<EntryDto>?>;

internal sealed class ListEntriesQueryHandler(
    AppDbContext dbContext, ICurrentUser currentUser, IStyleCatalog styleCatalog, IEntryTableAssignments entryTableAssignments)
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

        var styleCodes = entries.Select(e => e.StyleCode).Distinct(StringComparer.Ordinal).ToList();
        var styleByCode = await styleCatalog.GetByCodesAsync(styleCodes, cancellationToken);

        var categoryIds = entries.Where(e => e.CompetitionCategoryId.HasValue)
            .Select(e => e.CompetitionCategoryId!.Value).Distinct().ToList();
        var categoryNameById = categoryIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await dbContext.CompetitionCategories
                    .Where(c => categoryIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.Name })
                    .ToListAsync(cancellationToken))
                .ToDictionary(c => c.Id, c => c.Name);

        var entryIds = entries.Select(e => e.Id).ToList();
        var tableByEntryId = await entryTableAssignments.GetByEntryAsync(request.CompetitionId, entryIds, cancellationToken);

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
                    style?.AbvLow,
                    style?.AbvHigh,
                    e.AbvPercent,
                    e.BeerName,
                    e.NotValidForBos,
                    table?.TableId,
                    table?.TableName,
                    competitionCategoryName,
                    style?.CategoryNumber,
                    style?.CategoryName);
            })
            .OrderBy(e => e.BlindCode, StringComparer.CurrentCulture)
            .ToList();
    }
}
