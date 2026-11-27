using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Integration;

/// <summary>Judging's implementation of <see cref="IJudgeEngagement"/>: reads persisted seats and
/// evaluations only.</summary>
internal sealed class JudgeEngagement(AppDbContext dbContext) : IJudgeEngagement
{
    public async Task<IReadOnlySet<Guid>> GetEngagedJudgesAsync(
        Guid competitionId, IReadOnlyCollection<Guid> judgeIds, CancellationToken cancellationToken = default)
    {
        if (judgeIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = judgeIds.Distinct().ToList();
        var seated = dbContext.TableJudges
            .Where(tj => ids.Contains(tj.JudgeId) && tj.RemovedAt == null)
            .Join(dbContext.TastingTables.Where(t => t.CompetitionId == competitionId), tj => tj.TastingTableId, t => t.Id, (tj, _) => tj.JudgeId);
        var evaluated = dbContext.Evaluations
            .Where(e => ids.Contains(e.JudgeId))
            .Select(e => e.JudgeId);

        return (await seated.Union(evaluated).ToListAsync(cancellationToken)).ToHashSet();
    }
}
