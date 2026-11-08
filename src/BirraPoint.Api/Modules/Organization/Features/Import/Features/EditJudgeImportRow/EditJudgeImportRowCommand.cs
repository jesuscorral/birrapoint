using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Errors;
using BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents;
using BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents.Dtos;
using BuildingBlocks.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Import.Features.EditJudgeImportRow;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record EditJudgeImportRowCommand(Guid CompetitionId, Guid ImportId, int RowNumber, EditJudgeImportRowRequest Row)
    : IRequest<JudgeImportRowDto?>;

internal sealed class EditJudgeImportRowCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<EditJudgeImportRowCommand, JudgeImportRowDto?>
{
    public async Task<JudgeImportRowDto?> Handle(EditJudgeImportRowCommand request, CancellationToken cancellationToken)
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

        if (row.Status == JudgeImportRowStatus.Excluded)
        {
            // Excluded is terminal for a row (judge-import-file.md §Row validation outcomes) —
            // editing it back to Valid/Invalid here would silently un-exclude it.
            throw new OrganizationException(
                OrganizationError.InvalidImportFile,
                "An excluded row cannot be edited; re-upload the file to restore it.");
        }

        var edit = request.Row;
        row.Name = edit.Name;
        row.Email = edit.Email;
        row.BjcpRank = edit.BjcpRank;
        row.BjcpId = edit.BjcpId;
        row.PreferredCategory = edit.PreferredCategory;
        row.Preferences = edit.Preferences;

        var isComplete =
            !string.IsNullOrWhiteSpace(row.Name)
            && !string.IsNullOrWhiteSpace(row.Email)
            && ImportEmail.IsWellFormed(row.Email);

        if (isComplete)
        {
            row.Status = JudgeImportRowStatus.Valid;
            row.ErrorMessage = null;
        }
        else
        {
            row.Status = JudgeImportRowStatus.Invalid;
            row.ErrorMessage = "Row still has unresolved required fields (a name and a valid email).";
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return JudgeImportRowDto.FromEntity(row);
    }
}
