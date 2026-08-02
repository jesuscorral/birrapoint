using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Judges;

/// <summary>Full-replace editable row shape (contracts/judge-import-file.md §Full row edit).</summary>
public sealed record EditJudgeImportRowRequest(
    string? Name,
    string? Email,
    string? BjcpRank,
    string? BjcpId,
    string? PreferredCategory,
    string? Preferences);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record EditJudgeImportRowCommand(Guid CompetitionId, Guid ImportId, int RowNumber, EditJudgeImportRowRequest Row)
    : IRequest<JudgeImportRowDto?>;

/// <summary>
/// Unlike EditImportRowCommandValidator (Features/Import), neither Name nor Email is required at
/// the request level here — a full-replace edit is allowed to leave either missing (Status
/// recomputes to Invalid), per contracts/judge-import-file.md §Full row edit. There is no
/// separate mismatch/unresolved-reference workflow for judge-roster fields (no catalog involved)
/// that would justify forcing a stricter shape the way category/style do for beer entries.
/// </summary>
public sealed class EditJudgeImportRowCommandValidator : AbstractValidator<EditJudgeImportRowCommand>
{
    public EditJudgeImportRowCommandValidator()
    {
        RuleFor(c => c.Row).NotNull();

        When(c => c.Row is not null, () =>
        {
            RuleFor(c => c.Row.Name).MaximumLength(200);
            RuleFor(c => c.Row.Email).MaximumLength(320);
            RuleFor(c => c.Row.BjcpRank).MaximumLength(100);
            RuleFor(c => c.Row.BjcpId).MaximumLength(50);
            RuleFor(c => c.Row.PreferredCategory).MaximumLength(200);
            RuleFor(c => c.Row.Preferences).MaximumLength(2000);
        });
    }
}

public sealed class EditJudgeImportRowCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
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
            throw new DomainException(
                DomainErrorType.InvalidImportFile,
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
            && JudgeWorkbookParser.EmailPattern.IsMatch(row.Email);

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
