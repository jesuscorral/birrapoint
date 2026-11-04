using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents.Dtos;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents;

/// <summary>Builds the judge-facing view of one alert's totals — used both by the response of the
/// slice that raised/kept it open (SubmitEvaluation, AdjustEvaluation) and by GetMyDiscrepancies.</summary>
internal static class DiscrepancyViewBuilder
{
    public static async Task<DiscrepancyView> BuildAsync(
        AppDbContext dbContext, IJudgeDirectory judgeDirectory, Guid alertId, Guid tableId, Guid beerEntryId, Guid callerJudgeId, CancellationToken cancellationToken)
    {
        var blindCode = await dbContext.BeerEntries
            .Where(e => e.Id == beerEntryId)
            .Select(e => e.BlindCode)
            .SingleAsync(cancellationToken);

        var evaluations = await dbContext.Evaluations
            .Where(e => e.TastingTableId == tableId && e.BeerEntryId == beerEntryId)
            .Select(e => new { e.Id, e.Total, e.JudgeId })
            .ToListAsync(cancellationToken);

        var judgeNames = await judgeDirectory.GetDisplayNamesAsync(
            evaluations.Select(e => e.JudgeId).ToList(), cancellationToken);

        var totals = evaluations
            .Where(e => judgeNames.ContainsKey(e.JudgeId))
            .Select(e => new DiscrepancyTotalDto(judgeNames[e.JudgeId], e.Total, e.JudgeId == callerJudgeId, e.Id))
            .ToList();

        return new DiscrepancyView(alertId, blindCode, totals);
    }
}
