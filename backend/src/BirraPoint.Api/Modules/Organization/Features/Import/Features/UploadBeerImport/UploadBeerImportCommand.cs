using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Catalog.Contracts;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents;
using BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Import.Features.UploadBeerImport;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record UploadBeerImportCommand(Guid CompetitionId, IFormFile File) : IRequest<BeerImportBatchDto?>;

internal sealed class UploadBeerImportCommandHandler(AppDbContext dbContext, ICurrentUser currentUser, IStyleCatalog styleCatalog)
    : IRequestHandler<UploadBeerImportCommand, BeerImportBatchDto?>
{
    public async Task<BeerImportBatchDto?> Handle(UploadBeerImportCommand request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        // Entry import is organizer setup work — allowed only while it can still affect judging
        // (data-model.md §Competition state gates); the same 409 the wizard PUT already uses.
        if (competition.State is not (CompetitionState.Draft or CompetitionState.Active))
        {
            throw new DomainException(
                DomainErrorType.InvalidStateTransition,
                "Entries can only be imported while the competition is in Draft or Active state.");
        }

        var extension = Path.GetExtension(request.File.FileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(DomainErrorType.InvalidImportFile, "The uploaded file must be a .xlsx spreadsheet.");
        }

        var styles = (await styleCatalog.ListAsync(cancellationToken))
            .Select(style => new StyleCatalogEntry(style.Code, style.Name))
            .ToList();

        var categories = await dbContext.CompetitionCategories
            .Where(category => category.CompetitionId == competition.Id)
            .Select(category => new CategoryCatalogEntry(category.Id, category.Name))
            .ToListAsync(cancellationToken);

        var allowedPairs = await dbContext.CompetitionCategoryStyles
            .Where(pair => pair.CompetitionId == competition.Id)
            .Select(pair => new CategoryStyleCatalogEntry(pair.CompetitionCategoryId, pair.StyleCode))
            .ToListAsync(cancellationToken);

        await using var stream = request.File.OpenReadStream();
        var parsedRows = BeerWorkbookParser.Parse(stream, styles, categories, allowedPairs);

        // Single active batch per competition (contracts/import-file.md §Semantics): a new upload
        // discards whatever was left unconsolidated from a previous one.
        var priorBatch = await dbContext.BeerImportBatches
            .FirstOrDefaultAsync(
                batch => batch.CompetitionId == competition.Id && batch.Status == BeerImportBatchStatus.Pending,
                cancellationToken);

        if (priorBatch is not null)
        {
            dbContext.BeerImportBatches.Remove(priorBatch);
        }

        var newBatch = new BeerImportBatch { CompetitionId = competition.Id };
        foreach (var parsedRow in parsedRows)
        {
            newBatch.Rows.Add(new BeerImportRow
            {
                BeerImportBatchId = newBatch.Id,
                RowNumber = parsedRow.RowNumber,
                Status = parsedRow.Status,
                ParticipantName = parsedRow.ParticipantName,
                ParticipantEmail = parsedRow.ParticipantEmail,
                AcceMemberNumberText = parsedRow.AcceMemberNumber,
                DateOfBirth = parsedRow.DateOfBirth,
                Phone = parsedRow.Phone,
                CategoryText = parsedRow.CategoryText,
                ResolvedCompetitionCategoryId = parsedRow.ResolvedCompetitionCategoryId,
                StyleText = parsedRow.StyleText,
                ResolvedStyleCode = parsedRow.ResolvedStyleCode,
                SubmittedAt = parsedRow.SubmittedAt,
                AbvPercent = parsedRow.AbvPercent,
                BrewDate = parsedRow.BrewDate,
                BottlingDate = parsedRow.BottlingDate,
                Malts = parsedRow.Malts,
                Hops = parsedRow.Hops,
                Yeast = parsedRow.Yeast,
                OtherIngredients = parsedRow.OtherIngredients,
                EntryInstructions = parsedRow.EntryInstructions,
                ErrorMessage = parsedRow.ErrorMessage,
            });
        }

        dbContext.BeerImportBatches.Add(newBatch);
        await dbContext.SaveChangesAsync(cancellationToken);

        return BeerImportBatchDto.FromEntity(newBatch);
    }
}
