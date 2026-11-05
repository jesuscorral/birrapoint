using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Integration;

/// <summary>Organization's implementation of <see cref="ICompetitionAccess"/> over the shared DbContext.</summary>
internal sealed class CompetitionAccess(AppDbContext dbContext) : ICompetitionAccess
{
    public Task<CompetitionSnapshot?> GetOwnedAsync(
        Guid competitionId, string ownerUserId, CancellationToken cancellationToken = default) =>
        dbContext.Competitions
            .AsNoTracking()
            .Where(c => c.Id == competitionId && c.CreatedByUserId == ownerUserId)
            .Select(c => new CompetitionSnapshot(c.Id, c.Name, c.State))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<CompetitionSnapshot?> GetAsync(Guid competitionId, CancellationToken cancellationToken = default) =>
        dbContext.Competitions
            .AsNoTracking()
            .Where(c => c.Id == competitionId)
            .Select(c => new CompetitionSnapshot(c.Id, c.Name, c.State))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, CompetitionState>> GetStatesAsync(
        IReadOnlyCollection<Guid> competitionIds, CancellationToken cancellationToken = default)
    {
        if (competitionIds.Count == 0)
        {
            return new Dictionary<Guid, CompetitionState>();
        }

        var ids = competitionIds.Distinct().ToArray();
        return await dbContext.Competitions
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.State })
            .ToDictionaryAsync(c => c.Id, c => c.State, cancellationToken);
    }
}
