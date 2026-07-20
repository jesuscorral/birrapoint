using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Import;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record GetImportQuery(Guid CompetitionId, Guid ImportId) : IRequest<ImportBatchDto?>;

public sealed class GetImportQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<GetImportQuery, ImportBatchDto?>
{
    public async Task<ImportBatchDto?> Handle(GetImportQuery request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (!competitionExists)
        {
            return null;
        }

        var batch = await dbContext.ImportBatches
            .AsNoTracking()
            .Include(b => b.Rows)
            .FirstOrDefaultAsync(
                b => b.Id == request.ImportId && b.CompetitionId == request.CompetitionId, cancellationToken);

        return batch is null ? null : ImportBatchDto.FromEntity(batch);
    }
}
