using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Organization.Contracts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.TastingOrder.Features.GetMyTables;

/// <summary>GET /me/tables (contracts/rest-api.md §Judge workspace) — every table the caller is
/// actively assigned to, across every competition that has left Draft (FR-020/US6).</summary>
internal sealed record GetMyTablesQuery : IRequest<IReadOnlyList<JudgeTableSummaryDto>>;

internal sealed class GetMyTablesQueryHandler(AppDbContext dbContext, IJudgeDirectory judgeDirectory, ICompetitionAccess competitionAccess)
    : IRequestHandler<GetMyTablesQuery, IReadOnlyList<JudgeTableSummaryDto>>
{
    public async Task<IReadOnlyList<JudgeTableSummaryDto>> Handle(GetMyTablesQuery request, CancellationToken cancellationToken)
    {
        var judges = await judgeDirectory.GetSignedInJudgesAsync(cancellationToken);
        if (judges.Count == 0)
        {
            return [];
        }

        var judgeIds = judges.Select(j => j.Id).ToList();

        var tables = await dbContext.TableJudges
            .Where(tj => tj.RemovedAt == null && judgeIds.Contains(tj.JudgeId))
            .Join(dbContext.TastingTables, tj => tj.TastingTableId, t => t.Id, (_, t) => t)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        var states = await competitionAccess.GetStatesAsync(
            tables.Select(t => t.CompetitionId).Distinct().ToList(), cancellationToken);

        var rows = tables
            .Where(t => states.TryGetValue(t.CompetitionId, out var state) && state != CompetitionState.Draft)
            .Select(t => new { Table = t, State = states[t.CompetitionId] })
            .ToList();

        var fixerIds = rows
            .Where(r => r.Table.OrderFixedByJudgeId != null)
            .Select(r => r.Table.OrderFixedByJudgeId!.Value)
            .Distinct()
            .ToList();

        var fixerNames = await judgeDirectory.GetDisplayNamesAsync(fixerIds, cancellationToken);

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
