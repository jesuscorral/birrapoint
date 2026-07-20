using System.Text.Json;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Import;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record UploadImportCommand(Guid CompetitionId, IFormFile File) : IRequest<ImportBatchDto?>;

public sealed class UploadImportCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<UploadImportCommand, ImportBatchDto?>
{
    public async Task<ImportBatchDto?> Handle(UploadImportCommand request, CancellationToken cancellationToken)
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

        var styles = await dbContext.BjcpStyles
            .Select(style => new StyleCatalogEntry(style.Code, style.Name))
            .ToListAsync(cancellationToken);

        await using var stream = request.File.OpenReadStream();
        var parsedRows = WorkbookParser.Parse(stream, styles);

        // Single active batch per competition (contracts/import-file.md §Semantics): a new upload
        // discards whatever was left unconsolidated from a previous one.
        var priorBatch = await dbContext.ImportBatches
            .FirstOrDefaultAsync(
                batch => batch.CompetitionId == competition.Id && batch.Status == ImportBatchStatus.Pending,
                cancellationToken);

        if (priorBatch is not null)
        {
            dbContext.ImportBatches.Remove(priorBatch);
        }

        var newBatch = new ImportBatch { CompetitionId = competition.Id };
        foreach (var parsedRow in parsedRows)
        {
            newBatch.Rows.Add(new ImportRow
            {
                ImportBatchId = newBatch.Id,
                RowNumber = parsedRow.RowNumber,
                Status = parsedRow.Status,
                ParticipantName = parsedRow.ParticipantName,
                ParticipantEmail = parsedRow.ParticipantEmail,
                BeerName = parsedRow.BeerName,
                StyleText = parsedRow.StyleText,
                CollaboratorsJson = JsonSerializer.Serialize(parsedRow.Collaborators),
                ResolvedStyleCode = parsedRow.ResolvedStyleCode,
                ErrorMessage = parsedRow.ErrorMessage,
            });
        }

        dbContext.ImportBatches.Add(newBatch);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ImportBatchDto.FromEntity(newBatch);
    }
}
