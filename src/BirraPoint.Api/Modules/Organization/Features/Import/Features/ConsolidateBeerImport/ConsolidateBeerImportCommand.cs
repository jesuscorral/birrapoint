using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Domain;
using BirraPoint.Api.Modules.Organization.Errors;
using BuildingBlocks.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Features.Import.Features.ConsolidateBeerImport;

internal sealed record ConsolidatedEntryDto(Guid Id, string BlindCode, string StyleCode);

internal sealed record ConsolidateBeerImportResult(int Imported, int Excluded, IReadOnlyList<ConsolidatedEntryDto> Entries);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
internal sealed record ConsolidateBeerImportCommand(Guid CompetitionId, Guid ImportId) : IRequest<ConsolidateBeerImportResult?>;

internal sealed class ConsolidateBeerImportCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<ConsolidateBeerImportCommand, ConsolidateBeerImportResult?>
{
    public async Task<ConsolidateBeerImportResult?> Handle(ConsolidateBeerImportCommand request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        var batch = await dbContext.BeerImportBatches
            .Include(b => b.Rows)
            .FirstOrDefaultAsync(b => b.Id == request.ImportId && b.CompetitionId == competition.Id, cancellationToken);

        if (batch is null)
        {
            return null;
        }

        if (batch.Status != BeerImportBatchStatus.Pending)
        {
            throw new OrganizationException(
                OrganizationError.InvalidStateTransition,
                "This import has already been consolidated.");
        }

        var unresolvedRowNumbers = batch.Rows
            .Where(row => row.Status is BeerImportRowStatus.StyleMismatch or BeerImportRowStatus.CategoryMismatch
                or BeerImportRowStatus.CategoryStyleMismatch or BeerImportRowStatus.Invalid)
            .Select(row => row.RowNumber)
            .OrderBy(rowNumber => rowNumber)
            .ToList();

        if (unresolvedRowNumbers.Count > 0)
        {
            throw new OrganizationException(
                OrganizationError.UnresolvedImportRows,
                "The import has unresolved rows that must be corrected or excluded before consolidation.",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["rowNumbers"] = unresolvedRowNumbers });
        }

        var participantsByEmail = (await dbContext.Participants
                .Where(p => p.CompetitionId == competition.Id)
                .ToListAsync(cancellationToken))
            .ToDictionary(p => p.Email, StringComparer.OrdinalIgnoreCase);

        var existingBlindCodes = new HashSet<string>(await dbContext.BeerEntries
            .Where(e => e.CompetitionId == competition.Id)
            .Select(e => e.BlindCode)
            .ToListAsync(cancellationToken), StringComparer.Ordinal);

        var random = Random.Shared;
        var createdEntries = new List<ConsolidatedEntryDto>();

        foreach (var row in batch.Rows.Where(r => r.Status == BeerImportRowStatus.Valid).OrderBy(r => r.RowNumber))
        {
            if (participantsByEmail.TryGetValue(row.ParticipantEmail!, out var participant))
            {
                // Last-import-wins within this competition (still per-competition scoped — a
                // different competition's Participant row for the same email is untouched).
                participant.Name = row.ParticipantName!;
                participant.AcceMemberNumber = row.AcceMemberNumberText;
                participant.DateOfBirth = row.DateOfBirth;
                participant.Phone = row.Phone;
            }
            else
            {
                participant = new Participant
                {
                    CompetitionId = competition.Id,
                    Name = row.ParticipantName!,
                    Email = row.ParticipantEmail!,
                    AcceMemberNumber = row.AcceMemberNumberText,
                    DateOfBirth = row.DateOfBirth,
                    Phone = row.Phone,
                };
                dbContext.Participants.Add(participant);
                participantsByEmail[row.ParticipantEmail!] = participant;
            }

            var blindCode = BlindCodeGenerator.GenerateUnique(existingBlindCodes, random);

            var entry = new BeerEntry
            {
                CompetitionId = competition.Id,
                ParticipantId = participant.Id,
                BeerName = row.BeerName,
                StyleCode = row.ResolvedStyleCode!,
                BlindCode = blindCode,
                CompetitionCategoryId = row.ResolvedCompetitionCategoryId!.Value,
                SubmittedAt = row.SubmittedAt!.Value,
                AbvPercent = row.AbvPercent!.Value,
                BrewDate = row.BrewDate,
                BottlingDate = row.BottlingDate,
                Malts = row.Malts,
                Hops = row.Hops,
                Yeast = row.Yeast,
                OtherIngredients = row.OtherIngredients,
                EntryInstructions = row.EntryInstructions,
            };

            dbContext.BeerEntries.Add(entry);
            createdEntries.Add(new ConsolidatedEntryDto(entry.Id, entry.BlindCode, entry.StyleCode));
        }

        var excludedCount = batch.Rows.Count(r => r.Status == BeerImportRowStatus.Excluded);
        batch.Status = BeerImportBatchStatus.Consolidated;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ConsolidateBeerImportResult(createdEntries.Count, excludedCount, createdEntries);
    }
}
