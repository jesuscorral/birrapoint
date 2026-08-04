using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Judges;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record UploadJudgeImportCommand(Guid CompetitionId, IFormFile File) : IRequest<JudgeImportBatchDto?>;

public sealed class UploadJudgeImportCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<UploadJudgeImportCommand, JudgeImportBatchDto?>
{
    public async Task<JudgeImportBatchDto?> Handle(UploadJudgeImportCommand request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        // Judge roster import is organizer setup work — allowed only while it can still affect
        // provisioning (same Draft/Active gate as the beer-entry import, contracts/rest-api.md
        // §Judge Roster Import).
        if (competition.State is not (CompetitionState.Draft or CompetitionState.Active))
        {
            throw new DomainException(
                DomainErrorType.InvalidStateTransition,
                "Judges can only be imported while the competition is in Draft or Active state.");
        }

        var extension = Path.GetExtension(request.File.FileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(DomainErrorType.InvalidImportFile, "The uploaded file must be a .xlsx spreadsheet.");
        }

        await using var stream = request.File.OpenReadStream();
        var parsedRows = JudgeWorkbookParser.Parse(stream);

        // Single active judge-roster batch per competition (contracts/judge-import-file.md
        // §Semantics), independent of the beer-entry import's own single-active-batch rule.
        var priorBatch = await dbContext.JudgeImportBatches
            .FirstOrDefaultAsync(
                batch => batch.CompetitionId == competition.Id && batch.Status == JudgeImportBatchStatus.Pending,
                cancellationToken);

        if (priorBatch is not null)
        {
            dbContext.JudgeImportBatches.Remove(priorBatch);
        }

        var newBatch = new JudgeImportBatch { CompetitionId = competition.Id };
        foreach (var parsedRow in parsedRows)
        {
            newBatch.Rows.Add(new JudgeImportRow
            {
                JudgeImportBatchId = newBatch.Id,
                RowNumber = parsedRow.RowNumber,
                Status = parsedRow.Status,
                Name = parsedRow.Name,
                Email = parsedRow.Email,
                BjcpRank = parsedRow.BjcpRank,
                BjcpId = parsedRow.BjcpId,
                PreferredCategory = parsedRow.PreferredCategory,
                Preferences = parsedRow.Preferences,
                ErrorMessage = parsedRow.ErrorMessage,
            });
        }

        dbContext.JudgeImportBatches.Add(newBatch);
        await dbContext.SaveChangesAsync(cancellationToken);

        return JudgeImportBatchDto.FromEntity(newBatch);
    }
}
