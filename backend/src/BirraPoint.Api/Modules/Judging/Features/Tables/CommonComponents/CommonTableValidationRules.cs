using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents;

internal static class CommonTableValidationRules
{
    public static bool HaveNoDuplicates(IReadOnlyList<Guid> ids) => ids.Distinct().Count() == ids.Count;

    private static async Task<bool> IsOwnedCompetitionAsync(
        ICompetitionAccess competitionAccess, ICurrentUser currentUser, Guid competitionId, CancellationToken cancellationToken) =>
        await competitionAccess.GetOwnedAsync(competitionId, currentUser.Sub, cancellationToken) is not null;

    public static async Task<bool> IsNameUniqueAsync(
         AppDbContext dbContext,
         ICompetitionAccess competitionAccess,
         ICurrentUser currentUser,
         Guid competitionId,
         Guid? excludeTableId,
         string name,
         CancellationToken cancellationToken)
    {
        if (!await IsOwnedCompetitionAsync(competitionAccess, currentUser, competitionId, cancellationToken))
        {
            return true;
        }

        // Case-insensitive, evaluated in PostgreSQL (see LikePattern).
        var namePattern = LikePattern.Escape(name);
        return await dbContext.TastingTables.AllAsync(
            t => t.Id == excludeTableId
                || t.CompetitionId != competitionId
                || !EF.Functions.ILike(t.Name, namePattern, LikePattern.EscapeCharacter),
            cancellationToken);
    }

    public static async Task<bool> AllJudgesBelongToCompetitionAsync(
        AppDbContext dbContext,
        ICompetitionAccess competitionAccess,
        ICurrentUser currentUser,
        Guid competitionId,
        IReadOnlyList<Guid> judgeIds,
        CancellationToken cancellationToken)
    {
        if (judgeIds.Count == 0 || !await IsOwnedCompetitionAsync(competitionAccess, currentUser, competitionId, cancellationToken))
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
        ICompetitionAccess competitionAccess,
        ICurrentUser currentUser,
        Guid competitionId,
        Guid? excludeTableId,
        IReadOnlyList<Guid> beerEntryIds,
        CancellationToken cancellationToken)
    {
        if (beerEntryIds.Count == 0 || !await IsOwnedCompetitionAsync(competitionAccess, currentUser, competitionId, cancellationToken))
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
