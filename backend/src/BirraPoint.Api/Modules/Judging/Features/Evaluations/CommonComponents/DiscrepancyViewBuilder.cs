using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents.Dtos;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents;

/// <summary>Builds the judge-facing view of alerts' totals — used both by the response of the slice that
/// raised/kept one open (SubmitEvaluation, AdjustEvaluation) and by GetMyDiscrepancies. Constant number of
/// queries whatever the alert count.</summary>
internal static class DiscrepancyViewBuilder
{
    public static async Task<DiscrepancyView> BuildAsync(
        AppDbContext dbContext,
        IJudgeDirectory judgeDirectory,
        IEntryDirectory entryDirectory,
        Guid alertId, Guid tableId, Guid beerEntryId, Guid callerJudgeId, CancellationToken cancellationToken) =>
        (await BuildManyAsync(
            dbContext, judgeDirectory, entryDirectory, tableId, [(alertId, beerEntryId)], callerJudgeId, cancellationToken))
        .Single();

    /// <summary>One view per alert, in the given order.</summary>
    public static async Task<IReadOnlyList<DiscrepancyView>> BuildManyAsync(
        AppDbContext dbContext,
        IJudgeDirectory judgeDirectory,
        IEntryDirectory entryDirectory,
        Guid tableId,
        IReadOnlyList<(Guid AlertId, Guid BeerEntryId)> alerts,
        Guid callerJudgeId,
        CancellationToken cancellationToken)
    {
        if (alerts.Count == 0)
        {
            return [];
        }

        var entryIds = alerts.Select(a => a.BeerEntryId).Distinct().ToList();
        var samples = await entryDirectory.GetSamplesAsync(entryIds, cancellationToken);

        var evaluations = await dbContext.Evaluations
            .Where(e => e.TastingTableId == tableId && entryIds.Contains(e.BeerEntryId))
            .Select(e => new { e.Id, e.Total, e.JudgeId, e.BeerEntryId })
            .ToListAsync(cancellationToken);

        var judgeNames = await judgeDirectory.GetDisplayNamesAsync(
            evaluations.Select(e => e.JudgeId).Distinct().ToList(), cancellationToken);

        return alerts
            .Select(alert => new DiscrepancyView(
                alert.AlertId,
                samples[alert.BeerEntryId].BlindCode,
                evaluations
                    .Where(e => e.BeerEntryId == alert.BeerEntryId && judgeNames.ContainsKey(e.JudgeId))
                    .Select(e => new DiscrepancyTotalDto(judgeNames[e.JudgeId], e.Total, e.JudgeId == callerJudgeId, e.Id))
                    .ToList()))
            .ToList();
    }
}
