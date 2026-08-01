using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Import;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record ExcludeImportRowCommand(Guid CompetitionId, Guid ImportId, int RowNumber) : IRequest<ImportRowDto?>;

public sealed class ExcludeImportRowCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<ExcludeImportRowCommand, ImportRowDto?>
{
    public async Task<ImportRowDto?> Handle(ExcludeImportRowCommand request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (!competitionExists)
        {
            return null;
        }

        var batchExists = await dbContext.ImportBatches
            .AnyAsync(b => b.Id == request.ImportId && b.CompetitionId == request.CompetitionId, cancellationToken);

        if (!batchExists)
        {
            return null;
        }

        var row = await dbContext.ImportRows
            .FirstOrDefaultAsync(r => r.ImportBatchId == request.ImportId && r.RowNumber == request.RowNumber, cancellationToken);

        if (row is null)
        {
            return null;
        }

        row.Status = ImportRowStatus.Excluded;
        row.ErrorMessage = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ImportRowDto.FromEntity(row);
    }
}
