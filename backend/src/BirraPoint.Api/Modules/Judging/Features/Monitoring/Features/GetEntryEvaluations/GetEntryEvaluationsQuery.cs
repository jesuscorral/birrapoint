using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Contracts;
using BirraPoint.Api.Modules.Judging.Contracts.Dtos;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.Monitoring.Features.GetEntryEvaluations;

/// <summary>GET /competitions/{id}/entries/{entryId}/evaluations (contracts/rest-api.md §Monitoring
/// &amp; audit, FR-038/FR-042): read-only audit drill-down. Returns null when the competition
/// doesn't exist/isn't owned by the caller, the entry doesn't belong to the competition, or the
/// entry isn't yet assigned to a table (nothing to audit) — the endpoint maps all three to a plain
/// 404, never leaking existence.</summary>
internal sealed record GetEntryEvaluationsQuery(Guid CompetitionId, Guid EntryId) : IRequest<EntryEvaluationsResult?>;

/// <summary>Reuses EvaluationScoresDto/EvaluationCommentsDto from Modules/Judging/Features/Evaluations — same wire
/// shape, no need to redefine. Descriptors/Feedback (Session 2026-09-21) are the organizer's
/// read-only view of the same structured tasting-sheet data the judge filled in.</summary>
internal sealed record EvaluationAuditItemDto(
    string JudgeDisplayName, EvaluationScoresDto Scores, EvaluationCommentsDto Comments, int Total, string Status,
    EvaluationDescriptorsDto? Descriptors, string? Feedback);

internal sealed record EntryEvaluationsResult(string BlindCode, IReadOnlyList<EvaluationAuditItemDto> Evaluations, decimal? ConsolidatedMean);

internal sealed class GetEntryEvaluationsQueryHandler(
    AppDbContext dbContext, ICurrentUser currentUser, IStoredEvaluations storedEvaluations)
    : IRequestHandler<GetEntryEvaluationsQuery, EntryEvaluationsResult?>
{
    public async Task<EntryEvaluationsResult?> Handle(GetEntryEvaluationsQuery request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);
        if (!competitionExists)
        {
            return null;
        }

        var entry = await dbContext.BeerEntries
            .Where(e => e.Id == request.EntryId && e.CompetitionId == request.CompetitionId)
            .Select(e => new { e.BlindCode })
            .SingleOrDefaultAsync(cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var tableAssignment = await dbContext.TableSamples
            .Where(ts => ts.BeerEntryId == request.EntryId)
            .Join(dbContext.TastingTables, ts => ts.TastingTableId, t => t.Id, (ts, t) => new { t.State })
            .SingleOrDefaultAsync(cancellationToken);
        if (tableAssignment is null)
        {
            return null;
        }

        var evaluations = (await storedEvaluations.GetByEntriesAsync([request.EntryId], cancellationToken))[request.EntryId];

        var items = evaluations
            .Select(ev => new EvaluationAuditItemDto(
                ev.JudgeDisplayName,
                new EvaluationScoresDto(ev.AromaScore, ev.AppearanceScore, ev.FlavorScore, ev.MouthfeelScore, ev.OverallScore),
                new EvaluationCommentsDto(
                    ev.AromaComment, ev.AppearanceComment, ev.FlavorComment, ev.MouthfeelComment, ev.OverallComment),
                ev.Total,
                ev.Status,
                ev.Descriptors,
                ev.Feedback))
            .ToList();

        // Mirrors CloseTableRules.ComputeMean's rounding convention without importing across feature
        // folders (deliberate — small pure-arithmetic duplication over cross-slice coupling).
        decimal? consolidatedMean = null;
        if (tableAssignment.State == TableState.Closed)
        {
            consolidatedMean = evaluations.Count == 0
                ? 0m
                : Math.Round(evaluations.Average(x => (decimal)x.Total), 2, MidpointRounding.AwayFromZero);
        }

        return new EntryEvaluationsResult(entry.BlindCode, items, consolidatedMean);
    }
}
