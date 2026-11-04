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

internal sealed class GetMyDiscrepanciesQueryHandler(AppDbContext dbContext, IJudgeDirectory judgeDirectory)
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

        var views = new List<DiscrepancyView>();

        foreach (var alert in openAlerts)
        {
            var totals = await dbContext.Evaluations
                .Where(e => e.TastingTableId == request.TableId && e.BeerEntryId == alert.BeerEntryId)
                .ToDictionaryAsync(e => e.JudgeId, e => e.Total, cancellationToken);
            var involved = DiscrepancyRules.ComputeInvolvedJudgeIds(totals);

            if (!involved.Contains(judgeId.Value))
            {
                continue;
            }

            views.Add(await DiscrepancyViewBuilder.BuildAsync(
                dbContext, judgeDirectory, alert.Id, request.TableId, alert.BeerEntryId, judgeId.Value, cancellationToken));
        }

        return views;
    }
}
