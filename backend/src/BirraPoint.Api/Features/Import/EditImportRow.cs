using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Import;

/// <summary>Full-replace editable row shape (contracts/rest-api.md §Entry Import) — mirrors
/// <see cref="ImportRowDataDto"/> minus the read-only raw <c>Category</c>/<c>Style</c> cell text,
/// which stays whatever the file originally parsed.</summary>
public sealed record EditImportRowRequest(
    string? ParticipantName,
    string? ParticipantEmail,
    string? AcceMemberNumber,
    DateOnly? DateOfBirth,
    string? Phone,
    Guid? CompetitionCategoryId,
    string? StyleCode,
    DateTimeOffset? SubmittedAt,
    decimal? AbvPercent,
    DateOnly? BrewDate,
    DateOnly? BottlingDate,
    string? Malts,
    string? Hops,
    string? Yeast,
    string? OtherIngredients,
    string? EntryInstructions,
    string? BeerName);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record EditImportRowCommand(Guid CompetitionId, Guid ImportId, int RowNumber, EditImportRowRequest Row)
    : IRequest<ImportRowDto?>;

/// <summary>
/// ParticipantName/AbvPercent/SubmittedAt have no "unresolved" resolution workflow the way
/// category/style do (data-model.md §ImportRow) — a missing/malformed value here is simply a bad
/// request, mirroring SubmitEvaluationCommandValidator's NotNull checks. ParticipantEmail and the
/// category/style references are deliberately NOT required here: an incomplete edit is allowed to
/// save (Status recomputes to Invalid) so the organizer can make partial progress across several
/// saves on the Mapping &amp; Correction screen.
/// </summary>
public sealed class EditImportRowCommandValidator : AbstractValidator<EditImportRowCommand>
{
    public EditImportRowCommandValidator(AppDbContext dbContext)
    {
        RuleFor(c => c.Row).NotNull();

        When(c => c.Row is not null, () =>
        {
            RuleFor(c => c.Row.ParticipantName).NotEmpty().MaximumLength(200);
            RuleFor(c => c.Row.ParticipantEmail).MaximumLength(320);
            RuleFor(c => c.Row.AcceMemberNumber).MaximumLength(50);
            RuleFor(c => c.Row.Phone).MaximumLength(30);
            RuleFor(c => c.Row.AbvPercent).NotNull().InclusiveBetween(0, 99.99m);
            RuleFor(c => c.Row.SubmittedAt).NotNull();
            RuleFor(c => c.Row.Malts).MaximumLength(1000);
            RuleFor(c => c.Row.Hops).MaximumLength(1000);
            RuleFor(c => c.Row.Yeast).MaximumLength(1000);
            RuleFor(c => c.Row.OtherIngredients).MaximumLength(1000);
            RuleFor(c => c.Row.EntryInstructions).MaximumLength(1000);
            RuleFor(c => c.Row.BeerName).MaximumLength(200);

            RuleFor(c => c)
                .MustAsync(async (command, cancellationToken) =>
                    command.Row.CompetitionCategoryId is null
                    || await dbContext.CompetitionCategories.AnyAsync(
                        category => category.Id == command.Row.CompetitionCategoryId && category.CompetitionId == command.CompetitionId,
                        cancellationToken))
                .WithName(nameof(EditImportRowRequest.CompetitionCategoryId))
                .WithMessage("CompetitionCategoryId does not belong to this competition.");

            RuleFor(c => c)
                .MustAsync(async (command, cancellationToken) =>
                    string.IsNullOrEmpty(command.Row.StyleCode)
                    || await dbContext.BjcpStyles.AnyAsync(style => style.Code == command.Row.StyleCode, cancellationToken))
                .WithName(nameof(EditImportRowRequest.StyleCode))
                .WithMessage(c => $"Style code '{c.Row.StyleCode}' does not exist in the BJCP 2021 catalog.");
        });
    }
}

public sealed class EditImportRowCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<EditImportRowCommand, ImportRowDto?>
{
    public async Task<ImportRowDto?> Handle(EditImportRowCommand request, CancellationToken cancellationToken)
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

        if (row.Status == ImportRowStatus.Excluded)
        {
            // Excluded is terminal for a row (import-file.md §Row validation outcomes) — editing
            // it back to Valid/Invalid here would silently un-exclude it.
            throw new DomainException(
                DomainErrorType.InvalidImportFile,
                "An excluded row cannot be edited; re-upload the file to restore it.");
        }

        var edit = request.Row;
        row.ParticipantName = edit.ParticipantName;
        row.ParticipantEmail = edit.ParticipantEmail;
        row.AcceMemberNumberText = edit.AcceMemberNumber;
        row.DateOfBirth = edit.DateOfBirth;
        row.Phone = edit.Phone;
        row.ResolvedCompetitionCategoryId = edit.CompetitionCategoryId;
        row.ResolvedStyleCode = edit.StyleCode;
        row.SubmittedAt = edit.SubmittedAt;
        row.AbvPercent = edit.AbvPercent;
        row.BrewDate = edit.BrewDate;
        row.BottlingDate = edit.BottlingDate;
        row.Malts = edit.Malts;
        row.Hops = edit.Hops;
        row.Yeast = edit.Yeast;
        row.OtherIngredients = edit.OtherIngredients;
        row.EntryInstructions = edit.EntryInstructions;
        row.BeerName = edit.BeerName;

        var isComplete =
            !string.IsNullOrWhiteSpace(row.ParticipantEmail)
            && WorkbookParser.EmailPattern.IsMatch(row.ParticipantEmail)
            && row.ResolvedCompetitionCategoryId is not null
            && !string.IsNullOrWhiteSpace(row.ResolvedStyleCode);

        if (!isComplete)
        {
            row.Status = ImportRowStatus.Invalid;
            row.ErrorMessage = "Row still has unresolved required fields (a valid participant email, category, or style).";
        }
        else
        {
            // FR-053: category and style are each individually set (and the validator above
            // already confirmed both are real references) — still cross-check the pair itself.
            var isAllowedPair = await dbContext.CompetitionCategoryStyles.AnyAsync(
                pair => pair.CompetitionCategoryId == row.ResolvedCompetitionCategoryId && pair.StyleCode == row.ResolvedStyleCode,
                cancellationToken);

            if (isAllowedPair)
            {
                row.Status = ImportRowStatus.Valid;
                row.ErrorMessage = null;
            }
            else
            {
                var categoryName = await dbContext.CompetitionCategories
                    .Where(category => category.Id == row.ResolvedCompetitionCategoryId)
                    .Select(category => category.Name)
                    .FirstOrDefaultAsync(cancellationToken);

                row.Status = ImportRowStatus.CategoryStyleMismatch;
                row.ErrorMessage = $"Estilo '{row.ResolvedStyleCode}' is a valid BJCP style, but is not assigned to category '{categoryName}' in this competition.";
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return ImportRowDto.FromEntity(row);
    }
}
