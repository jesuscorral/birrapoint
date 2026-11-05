using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Integration;

/// <summary>Judging's implementation of <see cref="IJudgeEngagement"/>: reads persisted seats and
/// evaluations only, so pending changes to the excluded table never count.</summary>
internal sealed class JudgeEngagement(AppDbContext dbContext) : IJudgeEngagement
{
    public async Task<IReadOnlySet<Guid>> GetJudgesEngagedOutsideTableAsync(
        Guid excludedTableId, IReadOnlyCollection<Guid> judgeIds, CancellationToken cancellationToken = default)
    {
        if (judgeIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = judgeIds.Distinct().ToList();
        var seatedElsewhere = dbContext.TableJudges
            .Where(tj => ids.Contains(tj.JudgeId) && tj.TastingTableId != excludedTableId && tj.RemovedAt == null)
            .Select(tj => tj.JudgeId);
        var evaluated = dbContext.Evaluations
            .Where(e => ids.Contains(e.JudgeId))
            .Select(e => e.JudgeId);

        return (await seatedElsewhere.Union(evaluated).ToListAsync(cancellationToken)).ToHashSet();
    }
}
