using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Tables;

/// <summary>
/// Shared, DB-touching validation rules for CreateTable/UpdateTable (400s, not COI). Scoped to a
/// competition the caller owns — if they don't own it, every check here passes trivially and the
/// handler's own ownership check produces the 404 for the request as a whole (mirrors
/// UpdateJudgeEmailCommandValidator's ownership scoping, so a non-owner can't learn about another
/// organizer's tables/judges/entries from validation error content).
/// </summary>
internal static class TableValidationRules
{
    public static bool HaveNoDuplicates(IReadOnlyList<Guid> ids) => ids.Distinct().Count() == ids.Count;

    private static Task<bool> IsOwnedCompetitionAsync(
        AppDbContext dbContext, ICurrentUser currentUser, Guid competitionId, CancellationToken cancellationToken) =>
        dbContext.Competitions.AnyAsync(c => c.Id == competitionId && c.CreatedByUserId == currentUser.Sub, cancellationToken);

    public static async Task<bool> IsNameUniqueAsync(
        AppDbContext dbContext,
        ICurrentUser currentUser,
        Guid competitionId,
        Guid? excludeTableId,
        string name,
        CancellationToken cancellationToken)
    {
        if (!await IsOwnedCompetitionAsync(dbContext, currentUser, competitionId, cancellationToken))
        {
            return true;
        }

        return await dbContext.TastingTables.AllAsync(
            t => t.Id == excludeTableId
                || t.CompetitionId != competitionId
                || t.Name.ToLower() != name.ToLower(),
            cancellationToken);
    }

    public static async Task<bool> AllJudgesBelongToCompetitionAsync(
        AppDbContext dbContext,
        ICurrentUser currentUser,
        Guid competitionId,
        IReadOnlyList<Guid> judgeIds,
        CancellationToken cancellationToken)
    {
        if (judgeIds.Count == 0 || !await IsOwnedCompetitionAsync(dbContext, currentUser, competitionId, cancellationToken))
        {
            return true;
        }

        var distinctIds = judgeIds.Distinct().ToList();
        var matchCount = await dbContext.Judges.CountAsync(
            j => j.CompetitionId == competitionId && distinctIds.Contains(j.Id), cancellationToken);

        return matchCount == distinctIds.Count;
    }

    /// <summary>An entry is assignable when it belongs to this competition and is unassigned or
    /// already assigned to <paramref name="excludeTableId"/> (the table being edited, if any).</summary>
    public static async Task<bool> AllEntriesAreAssignableAsync(
        AppDbContext dbContext,
        ICurrentUser currentUser,
        Guid competitionId,
        Guid? excludeTableId,
        IReadOnlyList<Guid> beerEntryIds,
        CancellationToken cancellationToken)
    {
        if (beerEntryIds.Count == 0 || !await IsOwnedCompetitionAsync(dbContext, currentUser, competitionId, cancellationToken))
        {
            return true;
        }

        var distinctIds = beerEntryIds.Distinct().ToList();

        var validEntryCount = await dbContext.BeerEntries
            .CountAsync(e => e.CompetitionId == competitionId && distinctIds.Contains(e.Id), cancellationToken);

        if (validEntryCount != distinctIds.Count)
        {
            return false;
        }

        return await dbContext.TableSamples.AllAsync(
            ts => ts.TastingTableId == excludeTableId || !distinctIds.Contains(ts.BeerEntryId),
            cancellationToken);
    }
}
