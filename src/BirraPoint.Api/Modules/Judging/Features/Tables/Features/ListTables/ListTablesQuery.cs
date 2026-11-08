using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Catalog.Contracts;
using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents;
using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents.Dtos;
using BirraPoint.Api.Modules.Organization.Contracts;
using BuildingBlocks.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.Features.ListTables;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record ListTablesQuery(Guid CompetitionId) : IRequest<IReadOnlyList<TableDto>?>;

internal sealed class ListTablesQueryHandler(AppDbContext dbContext, ICurrentUser currentUser, ICompetitionAccess competitionAccess, IStyleCatalog styleCatalog,
    IJudgeDirectory judgeDirectory, IEntryDirectory entryDirectory)
    : IRequestHandler<ListTablesQuery, IReadOnlyList<TableDto>?>
{
    public async Task<IReadOnlyList<TableDto>?> Handle(ListTablesQuery request, CancellationToken cancellationToken)
    {
        if (await competitionAccess.GetOwnedAsync(request.CompetitionId, currentUser.Sub, cancellationToken) is null)
        {
            return null;
        }

        var tableIds = await dbContext.TastingTables
            .Where(t => t.CompetitionId == request.CompetitionId)
            .OrderBy(t => t.Name)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        return await TableProjector.ProjectManyAsync(dbContext, styleCatalog, judgeDirectory, entryDirectory, tableIds, cancellationToken);
    }
}
