using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents;
using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents.Dtos;
using BirraPoint.Api.Modules.Judging.Features.TastingOrder.CommonComponents;
using BirraPoint.Api.Modules.Organization.Contracts;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.Evaluations.Features.GetMyDiscrepancies;

/// <summary>GET /me/tables/{tableId}/discrepancies (contracts/rest-api.md §Judge workspace).
/// Returns null when the caller is not an active member of this table.</summary>
internal sealed record GetMyDiscrepanciesQuery(Guid TableId) : IRequest<IReadOnlyList<DiscrepancyView>?>;

internal sealed class GetMyDiscrepanciesQueryHandler(AppDbContext dbContext, IJudgeDirectory judgeDirectory, IEntryDirectory entryDirectory)
    : IRequestHandler<GetMyDiscrepanciesQuery, IReadOnlyList<DiscrepancyView>?>
{
    public async Task<IReadOnlyList<DiscrepancyView>?> Handle(GetMyDiscrepanciesQuery request, CancellationToken cancellationToken)
    {
        var judges = await judgeDirectory.GetSignedInJudgesAsync(cancellationToken);
        var judgeId = await JudgeTableAccess.FindActiveMembershipAsync(dbContext, judges, request.TableId, cancellationToken);
        if (judgeId is null)
        {
            return null;
        }

        var openAlerts = await dbContext.DiscrepancyAlerts
            .Where(a => a.TastingTableId == request.TableId && a.Status == DiscrepancyStatus.Open)
            .ToListAsync(cancellationToken);

        var alertEntryIds = openAlerts.Select(a => a.BeerEntryId).ToList();
        var totalsByEntryId = (await dbContext.Evaluations
                .Where(e => e.TastingTableId == request.TableId && alertEntryIds.Contains(e.BeerEntryId))
                .Select(e => new { e.BeerEntryId, e.JudgeId, e.Total })
                .ToListAsync(cancellationToken))
            .GroupBy(e => e.BeerEntryId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<Guid, int>)g.ToDictionary(e => e.JudgeId, e => e.Total));

        var involvingCaller = openAlerts
            .Where(alert => DiscrepancyRules
                .ComputeInvolvedJudgeIds(totalsByEntryId.GetValueOrDefault(alert.BeerEntryId) ?? new Dictionary<Guid, int>())
                .Contains(judgeId.Value))
            .Select(alert => (alert.Id, alert.BeerEntryId))
            .ToList();

        return await DiscrepancyViewBuilder.BuildManyAsync(
            dbContext, judgeDirectory, entryDirectory, request.TableId, involvingCaller, judgeId.Value, cancellationToken);
    }
}
