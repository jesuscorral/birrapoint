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

        var tableIds = tables.Select(t => t.Id).ToList();

        // Three grouped queries (constant round trips regardless of table count) instead of a
        // per-table loop (senior-code-reviewer finding on PR #24) — this feeds a live dashboard
        // organizers reload during an active event, so cost shouldn't grow linearly with table count.
        var judgeCounts = await dbContext.TableJudges
            .Where(tj => tableIds.Contains(tj.TastingTableId) && tj.RemovedAt == null)
            .GroupBy(tj => tj.TastingTableId)
            .Select(g => new { TableId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TableId, x => x.Count, cancellationToken);

        var sampleCounts = await dbContext.TableSamples
            .Where(ts => tableIds.Contains(ts.TastingTableId))
            .GroupBy(ts => ts.TastingTableId)
            .Select(g => new { TableId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TableId, x => x.Count, cancellationToken);

        var evaluationCounts = await dbContext.Evaluations
            .Where(e => tableIds.Contains(e.TastingTableId))
            .GroupBy(e => e.TastingTableId)
            .Select(g => new { TableId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TableId, x => x.Count, cancellationToken);

        return tables
            .Select(table =>
            {
                var expected = judgeCounts.GetValueOrDefault(table.Id) * sampleCounts.GetValueOrDefault(table.Id);
                var completed = evaluationCounts.GetValueOrDefault(table.Id);
                var percent = expected == 0 ? 0 : (int)Math.Round(completed * 100.0 / expected, MidpointRounding.AwayFromZero);
                return new TableProgressSummaryDto(table.Id, table.Name, table.State.ToString(), completed, expected, percent);
            })
            .OrderBy(r => r.Name)
            .ToList();
    }
}
