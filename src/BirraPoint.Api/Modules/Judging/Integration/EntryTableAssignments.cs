using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Integration;

internal sealed class EntryTableAssignments(AppDbContext dbContext) : IEntryTableAssignments
{
    public async Task<IReadOnlyDictionary<Guid, EntryTableAssignment>> GetByEntryAsync(
        Guid competitionId, IReadOnlyCollection<Guid> beerEntryIds, CancellationToken cancellationToken = default)
    {
        if (beerEntryIds.Count == 0)
        {
            return new Dictionary<Guid, EntryTableAssignment>();
        }

        var ids = beerEntryIds.Distinct().ToList();
        var rows = await dbContext.TableSamples
            .AsNoTracking()
            .Where(ts => ids.Contains(ts.BeerEntryId))
            .Join(
                dbContext.TastingTables.Where(t => t.CompetitionId == competitionId),
                ts => ts.TastingTableId,
                t => t.Id,
                (ts, t) => new { ts.BeerEntryId, TableId = t.Id, TableName = t.Name })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.BeerEntryId, r => new EntryTableAssignment(r.TableId, r.TableName));
    }
}
