using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents.Dtos;
using BuildingBlocks.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Import.Features.GetBeerImport;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record GetBeerImportQuery(Guid CompetitionId, Guid ImportId) : IRequest<BeerImportBatchDto?>;

internal sealed class GetBeerImportQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<GetBeerImportQuery, BeerImportBatchDto?>
{
    public async Task<BeerImportBatchDto?> Handle(GetBeerImportQuery request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (!competitionExists)
        {
            return null;
        }

        var batch = await dbContext.BeerImportBatches
            .AsNoTracking()
            .Include(b => b.Rows)
            .FirstOrDefaultAsync(
                b => b.Id == request.ImportId && b.CompetitionId == request.CompetitionId, cancellationToken);

        return batch is null ? null : BeerImportBatchDto.FromEntity(batch);
    }
}
