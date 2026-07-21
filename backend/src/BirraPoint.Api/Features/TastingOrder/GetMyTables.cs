using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.TastingOrder;

/// <summary>GET /me/tables (contracts/rest-api.md §Judge workspace) — every table the caller is
/// actively assigned to, across every competition that has left Draft (FR-020/US6).</summary>
public sealed record GetMyTablesQuery : IRequest<IReadOnlyList<JudgeTableSummaryDto>>;

public sealed class GetMyTablesQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<GetMyTablesQuery, IReadOnlyList<JudgeTableSummaryDto>>
{
    public async Task<IReadOnlyList<JudgeTableSummaryDto>> Handle(GetMyTablesQuery request, CancellationToken cancellationToken)
    {
        var judges = await currentUser.GetJudgeRecordsAsync(cancellationToken);
        if (judges.Count == 0)
        {
            return [];
        }

        var judgeIds = judges.Select(j => j.Id).ToList();

        var rows = await dbContext.TableJudges
            .Where(tj => tj.RemovedAt == null && judgeIds.Contains(tj.JudgeId))
            .Join(dbContext.TastingTables, tj => tj.TastingTableId, t => t.Id, (_, t) => t)
            .Join(dbContext.Competitions, t => t.CompetitionId, c => c.Id, (t, c) => new { Table = t, c.State })
            .Where(x => x.State != CompetitionState.Draft)
            .OrderBy(x => x.Table.Name)
            .ToListAsync(cancellationToken);

        var fixerIds = rows
            .Where(r => r.Table.OrderFixedByJudgeId != null)
            .Select(r => r.Table.OrderFixedByJudgeId!.Value)
            .Distinct()
            .ToList();

        var fixerNames = fixerIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Judges
                .Where(j => fixerIds.Contains(j.Id))
                .ToDictionaryAsync(j => j.Id, j => j.DisplayName, cancellationToken);

        return rows
            .Select(r => new JudgeTableSummaryDto(
                r.Table.Id,
                r.Table.Name,
                r.State.ToString(),
                r.Table.State.ToString(),
                r.Table.OrderFixedByJudgeId != null,
                r.Table.OrderFixedByJudgeId is { } fixerId ? fixerNames.GetValueOrDefault(fixerId) : null))
            .ToList();
    }
}
