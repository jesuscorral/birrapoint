using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Common.Auth;

/// <summary>
/// Backfills a Judge row's Keycloak identity the first time its holder authenticates. Matches
/// every Judge row across competitions sharing <paramref name="email"/> — the COI key
/// (CompetitionId, Email) is not globally unique, so one person can judge several competitions.
/// Takes primitives rather than <see cref="ICurrentUser"/>, since <see cref="CurrentUser"/> calls
/// into this and an <see cref="ICurrentUser"/> dependency here would create a DI cycle.
/// </summary>
public interface IJudgeResolver
{
    Task<IReadOnlyList<Judge>> ResolveAndBackfillAsync(
        string sub, string? email, string? name, CancellationToken ct = default);
}

public sealed class JudgeResolver(AppDbContext db) : IJudgeResolver
{
    public async Task<IReadOnlyList<Judge>> ResolveAndBackfillAsync(
        string sub, string? email, string? name, CancellationToken ct = default)
    {
        var judges = await db.Judges.Where(judge => judge.Email == email).ToListAsync(ct);

        foreach (var judge in judges)
        {
            if (judge.KeycloakUserId is null)
            {
                judge.KeycloakUserId = sub;
                if (name is not null)
                {
                    judge.DisplayName = name;
                }
            }
        }

        return judges;
    }
}
