using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Judges;

public sealed record ConsolidatedJudgeDto(Guid Id, string Email);

public sealed record ConsolidateJudgeImportResult(
    IReadOnlyList<ConsolidatedJudgeDto> Created, IReadOnlyList<ConsolidatedJudgeDto> Updated, int Excluded);

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record ConsolidateJudgeImportCommand(Guid CompetitionId, Guid ImportId) : IRequest<ConsolidateJudgeImportResult?>;

public sealed class ConsolidateJudgeImportCommandHandler(
    AppDbContext dbContext, ICurrentUser currentUser, IDispatchJobQueue dispatchJobQueue)
    : IRequestHandler<ConsolidateJudgeImportCommand, ConsolidateJudgeImportResult?>
{
    public async Task<ConsolidateJudgeImportResult?> Handle(ConsolidateJudgeImportCommand request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        var batch = await dbContext.JudgeImportBatches
            .Include(b => b.Rows)
            .FirstOrDefaultAsync(b => b.Id == request.ImportId && b.CompetitionId == competition.Id, cancellationToken);

        if (batch is null)
        {
            return null;
        }

        if (batch.Status != JudgeImportBatchStatus.Pending)
        {
            throw new DomainException(
                DomainErrorType.InvalidStateTransition,
                "This judge import has already been consolidated.");
        }

        var unresolvedRowNumbers = batch.Rows
            .Where(row => row.Status == JudgeImportRowStatus.Invalid)
            .Select(row => row.RowNumber)
            .OrderBy(rowNumber => rowNumber)
            .ToList();

        if (unresolvedRowNumbers.Count > 0)
        {
            throw new DomainException(
                DomainErrorType.UnresolvedImportRows,
                "The judge import has unresolved rows that must be corrected or excluded before consolidation.",
                new Dictionary<string, object?> { ["rowNumbers"] = unresolvedRowNumbers });
        }

        var existingJudges = await dbContext.Judges
            .Where(j => j.CompetitionId == competition.Id)
            .ToListAsync(cancellationToken);
        var judgesByEmail = existingJudges.ToDictionary(j => j.Email, StringComparer.OrdinalIgnoreCase);

        var existingJudgeIds = existingJudges.Select(j => j.Id).ToHashSet();
        var judgeIdsWithInvitation = new HashSet<Guid>(await dbContext.Invitations
            .Where(i => existingJudgeIds.Contains(i.JudgeId))
            .Select(i => i.JudgeId)
            .ToListAsync(cancellationToken));

        var createdJudges = new List<ConsolidatedJudgeDto>();
        var updatedJudges = new List<ConsolidatedJudgeDto>();
        var judgesToProvision = new List<Judge>();
        var seenEmailsInBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in batch.Rows.Where(r => r.Status == JudgeImportRowStatus.Valid).OrderBy(r => r.RowNumber))
        {
            var email = row.Email!;
            var isFirstOccurrenceInBatch = seenEmailsInBatch.Add(email);

            if (judgesByEmail.TryGetValue(email, out var judge))
            {
                // Last-import-wins within this competition — same policy as beer-entry
                // Participant matching (ConsolidateImport.cs). Duplicate emails within the same
                // file resolve to a single upsert (FR-058): the later row's values win since rows
                // are processed in RowNumber order.
                judge.DisplayName = row.Name!;
                judge.BjcpRank = row.BjcpRank;
                judge.BjcpId = row.BjcpId;
                judge.PreferredCategory = row.PreferredCategory;
                judge.Preferences = row.Preferences;

                if (isFirstOccurrenceInBatch)
                {
                    updatedJudges.Add(new ConsolidatedJudgeDto(judge.Id, judge.Email));
                    judgesToProvision.Add(judge);
                }
            }
            else
            {
                judge = new Judge
                {
                    CompetitionId = competition.Id,
                    Email = email,
                    DisplayName = row.Name!,
                    BjcpRank = row.BjcpRank,
                    BjcpId = row.BjcpId,
                    PreferredCategory = row.PreferredCategory,
                    Preferences = row.Preferences,
                };
                dbContext.Judges.Add(judge);
                judgesByEmail[email] = judge;
                createdJudges.Add(new ConsolidatedJudgeDto(judge.Id, judge.Email));
                judgesToProvision.Add(judge);
            }

            if (judgeIdsWithInvitation.Add(judge.Id))
            {
                dbContext.Invitations.Add(new Invitation { JudgeId = judge.Id });
            }
        }

        var excludedCount = batch.Rows.Count(r => r.Status == JudgeImportRowStatus.Excluded);
        batch.Status = JudgeImportBatchStatus.Consolidated;

        await dbContext.SaveChangesAsync(cancellationToken);

        // Enqueued only after the SaveChangesAsync above commits (same convention as
        // RegisterJudges.cs/IDispatchJobQueue elsewhere) — ProvisionJudgeAccount only, never
        // SendInvitation (R-20/FR-057).
        foreach (var judge in judgesToProvision)
        {
            await dispatchJobQueue.EnqueueAsync(
                competition.Id, DispatchJobType.ProvisionJudgeAccount, new { JudgeId = judge.Id }, cancellationToken);
        }

        return new ConsolidateJudgeImportResult(createdJudges, updatedJudges, excludedCount);
    }
}
