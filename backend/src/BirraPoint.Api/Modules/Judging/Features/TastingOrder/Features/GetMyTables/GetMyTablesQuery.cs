using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Organization.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.TastingOrder.Features.GetMyTables;

/// <summary>GET /me/tables (contracts/rest-api.md §Judge workspace) — every table the caller is
/// actively assigned to, across every competition that has left Draft (FR-020/US6).</summary>
internal sealed record GetMyTablesQuery : IRequest<IReadOnlyList<JudgeTableSummaryDto>>;

internal sealed class GetMyTablesQueryHandler(AppDbContext dbContext, ICurrentUser currentUser)
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

/// <summary>
/// BR-01/FR-019 anonymity boundary (data-model.md §Anonymity boundary) — this file MUST NOT ever
/// gain a property for entrant data (BeerName, Participant.*, EntryCollaborator.*). Every
/// judge-facing query in this slice projects exclusively into these two shapes; a contract test
/// (OrderApiTests.GetTableSamples_payload_never_contains_entrant_fields) asserts the serialized
/// wire payload, not just this DTO's declared members.
/// </summary>
internal sealed record JudgeTableSummaryDto(
    Guid TableId,
    string Name,
    string CompetitionState,
    string TableState,
    bool OrderFixed,
    string? OrderFixedBy);
