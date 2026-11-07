using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Errors;
using BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents;
using BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents.Dtos;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Import.Features.EditBeerImportRow;

/// <summary>Full-replace editable row shape (contracts/rest-api.md §Entry Import) — mirrors
/// <see cref="BeerImportRowDataDto"/> minus the read-only raw <c>Category</c>/<c>Style</c> cell text,
/// which stays whatever the file originally parsed.</summary>
internal sealed record EditBeerImportRowRequest(
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
internal sealed record EditBeerImportRowCommand(Guid CompetitionId, Guid ImportId, int RowNumber, EditBeerImportRowRequest Row)
    : IRequest<BeerImportRowDto?>;

internal sealed class EditBeerImportRowCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<EditBeerImportRowCommand, BeerImportRowDto?>
{
    public async Task<BeerImportRowDto?> Handle(EditBeerImportRowCommand request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (!competitionExists)
        {
            return null;
        }

        var batchExists = await dbContext.BeerImportBatches
            .AnyAsync(b => b.Id == request.ImportId && b.CompetitionId == request.CompetitionId, cancellationToken);

        if (!batchExists)
        {
            return null;
        }

        var row = await dbContext.BeerImportRows
            .FirstOrDefaultAsync(r => r.BeerImportBatchId == request.ImportId && r.RowNumber == request.RowNumber, cancellationToken);

        if (row is null)
        {
            return null;
        }

        if (row.Status == BeerImportRowStatus.Excluded)
        {
            // Excluded is terminal for a row (import-file.md §Row validation outcomes) — editing
            // it back to Valid/Invalid here would silently un-exclude it.
            throw new OrganizationException(
                OrganizationError.InvalidImportFile,
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
            && BeerWorkbookParser.EmailPattern.IsMatch(row.ParticipantEmail)
            && row.ResolvedCompetitionCategoryId is not null
            && !string.IsNullOrWhiteSpace(row.ResolvedStyleCode);

        if (!isComplete)
        {
            row.Status = BeerImportRowStatus.Invalid;
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
                row.Status = BeerImportRowStatus.Valid;
                row.ErrorMessage = null;
            }
            else
            {
                var categoryName = await dbContext.CompetitionCategories
                    .Where(category => category.Id == row.ResolvedCompetitionCategoryId)
                    .Select(category => category.Name)
                    .FirstOrDefaultAsync(cancellationToken);

                row.Status = BeerImportRowStatus.CategoryStyleMismatch;
                row.ErrorMessage = $"Estilo '{row.ResolvedStyleCode}' is a valid BJCP style, but is not assigned to category '{categoryName}' in this competition.";
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return BeerImportRowDto.FromEntity(row);
    }
}
