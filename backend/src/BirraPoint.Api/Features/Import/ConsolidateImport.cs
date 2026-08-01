using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Import;

public sealed record ConsolidatedEntryDto(Guid Id, string BlindCode, string StyleCode);

public sealed record ConsolidateImportResult(int Imported, int Excluded, IReadOnlyList<ConsolidatedEntryDto> Entries);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record ConsolidateImportCommand(Guid CompetitionId, Guid ImportId) : IRequest<ConsolidateImportResult?>;

public sealed class ConsolidateImportCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<ConsolidateImportCommand, ConsolidateImportResult?>
{
    public async Task<ConsolidateImportResult?> Handle(ConsolidateImportCommand request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        var batch = await dbContext.ImportBatches
            .Include(b => b.Rows)
            .FirstOrDefaultAsync(b => b.Id == request.ImportId && b.CompetitionId == competition.Id, cancellationToken);

        if (batch is null)
        {
            return null;
        }

        if (batch.Status != ImportBatchStatus.Pending)
        {
            throw new DomainException(
                DomainErrorType.InvalidStateTransition,
                "This import has already been consolidated.");
        }

        var unresolvedRowNumbers = batch.Rows
            .Where(row => row.Status is ImportRowStatus.StyleMismatch or ImportRowStatus.CategoryMismatch
                or ImportRowStatus.CategoryStyleMismatch or ImportRowStatus.Invalid)
            .Select(row => row.RowNumber)
            .OrderBy(rowNumber => rowNumber)
            .ToList();

        if (unresolvedRowNumbers.Count > 0)
        {
            throw new DomainException(
                DomainErrorType.UnresolvedImportRows,
                "The import has unresolved rows that must be corrected or excluded before consolidation.",
                new Dictionary<string, object?> { ["rowNumbers"] = unresolvedRowNumbers });
        }

        var participantsByEmail = (await dbContext.Participants
                .Where(p => p.CompetitionId == competition.Id)
                .ToListAsync(cancellationToken))
            .ToDictionary(p => p.Email, StringComparer.OrdinalIgnoreCase);

        var existingBlindCodes = new HashSet<string>(await dbContext.BeerEntries
            .Where(e => e.CompetitionId == competition.Id)
            .Select(e => e.BlindCode)
            .ToListAsync(cancellationToken));

        var random = Random.Shared;
        var createdEntries = new List<ConsolidatedEntryDto>();

        foreach (var row in batch.Rows.Where(r => r.Status == ImportRowStatus.Valid).OrderBy(r => r.RowNumber))
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

        var excludedCount = batch.Rows.Count(r => r.Status == ImportRowStatus.Excluded);
        batch.Status = ImportBatchStatus.Consolidated;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new ConsolidateImportResult(createdEntries.Count, excludedCount, createdEntries);
    }
}
