using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Contracts;
using BirraPoint.Api.Modules.Judging.Contracts.Dtos;
using BirraPoint.Api.Modules.Judging.Features.Evaluations.CommonComponents;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Integration;

internal sealed class StoredEvaluations(AppDbContext dbContext, IJudgeDirectory judgeDirectory) : IStoredEvaluations
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<StoredEvaluationDto>>> GetByEntriesAsync(
        IReadOnlyCollection<Guid> beerEntryIds, CancellationToken cancellationToken = default)
    {
        if (beerEntryIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<StoredEvaluationDto>>();
        }

        var ids = beerEntryIds.Distinct().ToList();
        var evaluations = await dbContext.Evaluations
            .AsNoTracking()
            .Where(ev => ids.Contains(ev.BeerEntryId))
            .ToListAsync(cancellationToken);

        var judgeNames = await judgeDirectory.GetDisplayNamesAsync(
            evaluations.Select(ev => ev.JudgeId).Distinct().ToList(), cancellationToken);

        var byEntry = evaluations
            .Where(ev => judgeNames.ContainsKey(ev.JudgeId))
            .ToLookup(ev => ev.BeerEntryId);

        return ids.ToDictionary(
            id => id,
            id => (IReadOnlyList<StoredEvaluationDto>)byEntry[id]
                .Select(ev => new StoredEvaluationDto(
                    ev.BeerEntryId,
                    judgeNames[ev.JudgeId],
                    ev.AromaScore, ev.AromaComment,
                    ev.AppearanceScore, ev.AppearanceComment,
                    ev.FlavorScore, ev.FlavorComment,
                    ev.MouthfeelScore, ev.MouthfeelComment,
                    ev.OverallScore, ev.OverallComment,
                    ev.Total,
                    ev.Status.ToString(),
                    EvaluationDescriptorsSerializer.Deserialize(ev.DescriptorsJson),
                    ev.FeedbackComment))
                .ToList());
    }
}
