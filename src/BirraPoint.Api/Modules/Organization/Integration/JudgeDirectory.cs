using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Organization.Identity;
using BuildingBlocks.Identity;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Integration;

internal sealed class JudgeDirectory(AppDbContext dbContext, ICurrentUser currentUser, IJudgeResolver judgeResolver) : IJudgeDirectory
{
    public async Task<IReadOnlyList<JudgeSummary>> GetSignedInJudgesAsync(CancellationToken cancellationToken = default)
    {
        var judges = await judgeResolver.ResolveAndBackfillAsync(
            currentUser.Sub, currentUser.Email, currentUser.Name, cancellationToken);
        return judges
            .Select(j => new JudgeSummary(j.Id, j.CompetitionId, j.Email, j.DisplayName, j.BjcpRank))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> judgeIds, CancellationToken cancellationToken = default)
    {
        if (judgeIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var ids = judgeIds.Distinct().ToList();
        return await dbContext.Judges
            .AsNoTracking()
            .Where(j => ids.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.DisplayName, cancellationToken);
    }

    public async Task<IReadOnlyList<JudgeSummary>> GetSummariesAsync(
        IReadOnlyCollection<Guid> judgeIds, CancellationToken cancellationToken = default)
    {
        if (judgeIds.Count == 0)
        {
            return [];
        }

        var ids = judgeIds.Distinct().ToList();
        return await dbContext.Judges
            .AsNoTracking()
            .Where(j => ids.Contains(j.Id))
            .OrderBy(j => j.DisplayName)
            .Select(j => new JudgeSummary(j.Id, j.CompetitionId, j.Email, j.DisplayName, j.BjcpRank))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetCompetitionJudgeIdsAsync(
        Guid competitionId, IReadOnlyCollection<Guid> judgeIds, CancellationToken cancellationToken = default)
    {
        if (judgeIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = judgeIds.Distinct().ToList();
        return (await dbContext.Judges
                .AsNoTracking()
                .Where(j => j.CompetitionId == competitionId && ids.Contains(j.Id))
                .Select(j => j.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }
}
