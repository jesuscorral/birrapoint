using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Monitoring;

/// <summary>GET /competitions/{id}/progress (contracts/rest-api.md §Monitoring &amp; audit, T068):
/// initial dashboard state, deltas afterwards arrive via the EvaluationCompleted SignalR event.
/// Returns null when not found or not owned by the caller — the endpoint maps that to a plain 404
/// (same convention as ListEntriesQuery).</summary>
public sealed record GetProgressQuery(Guid CompetitionId) : IRequest<IReadOnlyList<TableProgressSummaryDto>?>;

/// <summary>Per-table snapshot. Completed/expected/percent use the exact same formula as
/// SubmitEvaluation.ComputeTableProgressAsync so this fetched row and a live-updated one via
/// EvaluationCompleted never disagree.</summary>
public sealed record TableProgressSummaryDto(Guid TableId, string Name, string State, int Completed, int Expected, int Percent);

public sealed class GetProgressQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<GetProgressQuery, IReadOnlyList<TableProgressSummaryDto>?>
{
    public async Task<IReadOnlyList<TableProgressSummaryDto>?> Handle(GetProgressQuery request, CancellationToken cancellationToken)
    {
        var competitionExists = await dbContext.Competitions
            .AnyAsync(c => c.Id == request.CompetitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);
        if (!competitionExists)
        {
            return null;
        }

        var tables = await dbContext.TastingTables
            .Where(t => t.CompetitionId == request.CompetitionId)
            .Select(t => new { t.Id, t.Name, t.State })
            .ToListAsync(cancellationToken);

        var results = new List<TableProgressSummaryDto>();
        foreach (var table in tables)
        {
            var judgeCount = await dbContext.TableJudges
                .CountAsync(tj => tj.TastingTableId == table.Id && tj.RemovedAt == null, cancellationToken);
            var sampleCount = await dbContext.TableSamples.CountAsync(ts => ts.TastingTableId == table.Id, cancellationToken);
            var expected = judgeCount * sampleCount;
            var completed = await dbContext.Evaluations.CountAsync(e => e.TastingTableId == table.Id, cancellationToken);
            var percent = expected == 0 ? 0 : (int)Math.Round(completed * 100.0 / expected, MidpointRounding.AwayFromZero);

            results.Add(new TableProgressSummaryDto(table.Id, table.Name, table.State.ToString(), completed, expected, percent));
        }

        return results.OrderBy(r => r.Name).ToList();
    }
}
