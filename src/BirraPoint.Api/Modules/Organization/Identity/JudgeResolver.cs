using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Identity;

/// <summary>
/// Backfills a Judge row's Keycloak identity the first time its holder authenticates. Matches
/// every Judge row across competitions sharing <paramref name="email"/> — the COI key
/// (CompetitionId, Email) is not globally unique, so one person can judge several competitions.
/// Takes the caller's claims as primitives so it stays a plain data operation with no request-scoped
/// dependency.
/// </summary>
internal interface IJudgeResolver
{
    Task<IReadOnlyList<Judge>> ResolveAndBackfillAsync(
        string sub, string? email, string? name, CancellationToken ct = default);
}

internal sealed class JudgeResolver(AppDbContext db) : IJudgeResolver
{
    public async Task<IReadOnlyList<Judge>> ResolveAndBackfillAsync(
        string sub, string? email, string? name, CancellationToken ct = default)
    {
        var judges = await db.Judges.Where(judge => judge.Email == email).ToListAsync(ct);

        var changed = false;
        foreach (var judge in judges.Where(judge => judge.KeycloakUserId is null))
        {
            judge.KeycloakUserId = sub;
            if (name is not null)
            {
                judge.DisplayName = name;
            }
            changed = true;
        }

        // Unlike IAuditWriter (which deliberately defers to the caller's own SaveChangesAsync to
        // stay atomic with a business change), this backfill is its own independent unit of work —
        // IJudgeDirectory.GetSignedInJudgesAsync is a read, so callers have no
        // reason to expect they must SaveChanges afterward. Persist here, not in the caller.
        if (changed)
        {
            await db.SaveChangesAsync(ct);
        }

        return judges;
    }
}
