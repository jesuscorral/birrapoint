using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents.Dtos;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents;

/// <summary>Builds the judge-facing view of one alert's totals — used both by the response of the
/// slice that raised/kept it open (SubmitEvaluation, AdjustEvaluation) and by GetMyDiscrepancies.</summary>
internal static class DiscrepancyViewBuilder
{
    public static async Task<DiscrepancyView> BuildAsync(
        AppDbContext dbContext, Guid alertId, Guid tableId, Guid beerEntryId, Guid callerJudgeId, CancellationToken cancellationToken)
    {
        var blindCode = await dbContext.BeerEntries
            .Where(e => e.Id == beerEntryId)
            .Select(e => e.BlindCode)
            .SingleAsync(cancellationToken);

        var totals = await dbContext.Evaluations
            .Where(e => e.TastingTableId == tableId && e.BeerEntryId == beerEntryId)
            .Join(dbContext.Judges, e => e.JudgeId, j => j.Id, (e, j) => new { e.Id, e.Total, e.JudgeId, j.DisplayName })
            .Select(x => new DiscrepancyTotalDto(x.DisplayName, x.Total, x.JudgeId == callerJudgeId, x.Id))
            .ToListAsync(cancellationToken);

        return new DiscrepancyView(alertId, blindCode, totals);
    }
}
