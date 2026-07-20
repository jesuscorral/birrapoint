using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Tables;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record ListTablesQuery(Guid CompetitionId) : IRequest<IReadOnlyList<TableDto>?>;

public sealed class ListTablesQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<ListTablesQuery, IReadOnlyList<TableDto>?>
{
    public async Task<IReadOnlyList<TableDto>?> Handle(ListTablesQuery request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (!competitionExists)
        {
            return null;
        }

        var tableIds = await dbContext.TastingTables
            .Where(t => t.CompetitionId == request.CompetitionId)
            .OrderBy(t => t.Name)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var result = new List<TableDto>(tableIds.Count);
        foreach (var tableId in tableIds)
        {
            result.Add(await TableProjector.ProjectAsync(dbContext, tableId, cancellationToken));
        }

        return result;
    }
}
