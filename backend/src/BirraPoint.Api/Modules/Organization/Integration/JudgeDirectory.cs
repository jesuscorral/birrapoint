using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Integration;

internal sealed class JudgeDirectory(AppDbContext dbContext) : IJudgeDirectory
{
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
}
