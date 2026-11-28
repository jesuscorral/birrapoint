using BirraPoint.Api.Modules.Judging.Persistence;
using BirraPoint.Contracts.Judging;
using BirraPoint.Contracts.Organization;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Integration;

/// <summary>Judging's implementation of <see cref="ITableMembership"/>: the judge ids of the caller come from
/// Organization's directory, the seat from Judging's own table.</summary>
internal sealed class TableMembership(JudgingDbContext dbContext, IJudgeDirectory judgeDirectory) : ITableMembership
{
    public async Task<bool> IsActiveMemberAsync(
        Guid tableId, string keycloakUserId, string? email, CancellationToken cancellationToken = default)
    {
        var judgeIds = await judgeDirectory.FindJudgeIdsAsync(keycloakUserId, email, cancellationToken);
        if (judgeIds.Count == 0)
        {
            return false;
        }

        return await dbContext.TableJudges
            .AsNoTracking()
            .AnyAsync(tj => tj.TastingTableId == tableId && tj.RemovedAt == null && judgeIds.Contains(tj.JudgeId), cancellationToken);
    }
}
