using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.TastingOrder;

/// <summary>
/// Shared judge-membership check for GetMyTables/GetTableSamples/FixOrder. Mirrors
/// CompetitionHub.JoinTable's KeycloakUserId/email matching (Realtime/CompetitionHub.cs), but is
/// built on <see cref="Common.Auth.ICurrentUser.GetJudgeRecordsAsync"/> — which already resolves
/// every Judge row matching the caller's email and backfills KeycloakUserId — rather than
/// re-deriving sub/email matching against raw claims here.
/// </summary>
internal static class JudgeTableAccess
{
    /// <summary>The caller's Judge id holding an active (non-removed) TableJudge row at this
    /// table, or null when none of the caller's resolved Judge rows are an active member —
    /// callers map that to a plain 404 (never reveal whether the table exists).</summary>
    public static async Task<Guid?> FindActiveMembershipAsync(
        AppDbContext dbContext, IReadOnlyList<Judge> callerJudges, Guid tableId, CancellationToken cancellationToken)
    {
        if (callerJudges.Count == 0)
        {
            return null;
        }

        var judgeIds = callerJudges.Select(j => j.Id).ToList();

        return await dbContext.TableJudges
            .Where(tj => tj.TastingTableId == tableId && tj.RemovedAt == null && judgeIds.Contains(tj.JudgeId))
            .Select(tj => (Guid?)tj.JudgeId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
