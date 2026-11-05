using System.Collections.Concurrent;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Modules.Organization.Contracts;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents;

/// <summary>Resolves the caller's owned competition once per instance and competition id, so the several
/// async rules of one validation run share a single ownership query. Validators are scoped (one per
/// request), so the cache never outlives the request.</summary>
internal sealed class OwnedCompetitionLookup(ICompetitionAccess competitionAccess, ICurrentUser currentUser)
{
    private readonly ConcurrentDictionary<Guid, Task<CompetitionSnapshot?>> _cache = new();

    public Task<CompetitionSnapshot?> GetOwnedAsync(Guid competitionId, CancellationToken cancellationToken) =>
        _cache.GetOrAdd(competitionId, id => competitionAccess.GetOwnedAsync(id, currentUser.Sub, cancellationToken));
}
