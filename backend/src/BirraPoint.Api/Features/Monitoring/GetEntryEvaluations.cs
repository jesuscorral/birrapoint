using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Evaluations;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Monitoring;

/// <summary>GET /competitions/{id}/entries/{entryId}/evaluations (contracts/rest-api.md §Monitoring
/// &amp; audit, T069, FR-038/FR-042): read-only audit drill-down. Returns null when the competition
/// doesn't exist/isn't owned by the caller, the entry doesn't belong to the competition, or the
/// entry isn't yet assigned to a table (nothing to audit) — the endpoint maps all three to a plain
/// 404, never leaking existence.</summary>
public sealed record GetEntryEvaluationsQuery(Guid CompetitionId, Guid EntryId) : IRequest<EntryEvaluationsResult?>;

/// <summary>Reuses EvaluationScoresDto/EvaluationCommentsDto from Features/Evaluations — same wire
/// shape, no need to redefine.</summary>
public sealed record EvaluationAuditItemDto(
    string JudgeDisplayName, EvaluationScoresDto Scores, EvaluationCommentsDto Comments, int Total, string Status);

public sealed record EntryEvaluationsResult(string BlindCode, IReadOnlyList<EvaluationAuditItemDto> Evaluations, decimal? ConsolidatedMean);

public sealed class GetEntryEvaluationsQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
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

        var evaluations = await dbContext.Evaluations
            .Where(ev => ev.BeerEntryId == request.EntryId)
            .Join(dbContext.Judges, ev => ev.JudgeId, j => j.Id, (ev, j) => new { Evaluation = ev, j.DisplayName })
            .ToListAsync(cancellationToken);

        var items = evaluations
            .Select(x => new EvaluationAuditItemDto(
                x.DisplayName,
                new EvaluationScoresDto(
                    x.Evaluation.AromaScore, x.Evaluation.AppearanceScore, x.Evaluation.FlavorScore,
                    x.Evaluation.MouthfeelScore, x.Evaluation.OverallScore),
                new EvaluationCommentsDto(
                    x.Evaluation.AromaComment, x.Evaluation.AppearanceComment, x.Evaluation.FlavorComment,
                    x.Evaluation.MouthfeelComment, x.Evaluation.OverallComment),
                x.Evaluation.Total,
                x.Evaluation.Status.ToString()))
            .ToList();

        // Mirrors CloseTableRules.ComputeMean's rounding convention without importing across feature
        // folders (deliberate — small pure-arithmetic duplication over cross-slice coupling).
        decimal? consolidatedMean = tableAssignment.State == TableState.Closed
            ? (evaluations.Count == 0 ? 0m : Math.Round(evaluations.Average(x => (decimal)x.Evaluation.Total), 2, MidpointRounding.AwayFromZero))
            : null;

        return new EntryEvaluationsResult(entry.BlindCode, items, consolidatedMean);
    }
}
