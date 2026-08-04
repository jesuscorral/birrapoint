using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Judges;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record ExcludeJudgeImportRowCommand(Guid CompetitionId, Guid ImportId, int RowNumber) : IRequest<JudgeImportRowDto?>;

public sealed class ExcludeJudgeImportRowCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<ExcludeJudgeImportRowCommand, JudgeImportRowDto?>
{
    public async Task<JudgeImportRowDto?> Handle(ExcludeJudgeImportRowCommand request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (!competitionExists)
        {
            return null;
        }

        var batchExists = await dbContext.JudgeImportBatches
            .AnyAsync(b => b.Id == request.ImportId && b.CompetitionId == request.CompetitionId, cancellationToken);

        if (!batchExists)
        {
            return null;
        }

        var row = await dbContext.JudgeImportRows
            .FirstOrDefaultAsync(r => r.JudgeImportBatchId == request.ImportId && r.RowNumber == request.RowNumber, cancellationToken);

        if (row is null)
        {
            return null;
        }

        row.Status = JudgeImportRowStatus.Excluded;
        row.ErrorMessage = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        return JudgeImportRowDto.FromEntity(row);
    }
}
