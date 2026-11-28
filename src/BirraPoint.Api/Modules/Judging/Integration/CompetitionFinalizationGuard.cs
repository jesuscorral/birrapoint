using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Judging.Domain;
using BirraPoint.Api.Modules.Judging.Persistence;
using BirraPoint.Contracts.Organization;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Integration;

internal sealed class CompetitionFinalizationGuard(JudgingDbContext dbContext) : ICompetitionFinalizationGuard
{
    public async Task<IReadOnlyList<Guid>> GetOpenTableIdsAsync(Guid competitionId, CancellationToken cancellationToken = default) =>
        await dbContext.TastingTables
            .AsNoTracking()
            .Where(t => t.CompetitionId == competitionId && t.State != TableState.Closed)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
}
