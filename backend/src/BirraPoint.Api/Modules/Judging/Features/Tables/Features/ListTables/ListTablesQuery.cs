using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Catalog.Contracts;
using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents;
using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.Features.ListTables;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record ListTablesQuery(Guid CompetitionId) : IRequest<IReadOnlyList<TableDto>?>;

internal sealed class ListTablesQueryHandler(AppDbContext dbContext, ICurrentUser currentUser, IStyleCatalog styleCatalog)
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

        return await TableProjector.ProjectManyAsync(dbContext, styleCatalog, tableIds, cancellationToken);
    }
}
