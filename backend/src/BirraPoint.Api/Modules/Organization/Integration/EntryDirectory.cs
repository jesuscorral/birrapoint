using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Integration;

/// <summary>Projects only anonymous columns into the query (BR-01/FR-019): no entrant field is ever read.</summary>
internal sealed class EntryDirectory(AppDbContext dbContext) : IEntryDirectory
{
    public async Task<IReadOnlyDictionary<Guid, EntrySampleInfo>> GetSamplesAsync(
        IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken = default)
    {
        if (entryIds.Count == 0)
        {
            return new Dictionary<Guid, EntrySampleInfo>();
        }

        var ids = entryIds.Distinct().ToList();
        return await dbContext.BeerEntries
            .AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new EntrySampleInfo(
                e.Id,
                e.CompetitionId,
                e.BlindCode,
                e.StyleCode,
                e.AbvPercent,
                e.EntryInstructions,
                e.NotValidForBos,
                dbContext.CompetitionCategories
                    .Where(c => c.Id == e.CompetitionCategoryId)
                    .Select(c => c.Name)
                    .FirstOrDefault()))
            .ToDictionaryAsync(e => e.Id, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetCompetitionEntryIdsAsync(
        Guid competitionId, IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken = default)
    {
        if (entryIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = entryIds.Distinct().ToList();
        return (await dbContext.BeerEntries
                .AsNoTracking()
                .Where(e => e.CompetitionId == competitionId && ids.Contains(e.Id))
                .Select(e => e.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }
}
