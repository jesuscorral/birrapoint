using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Common.Auth;

/// <summary>
/// Resolves the <see cref="Organizer"/> row for the current caller, creating it lazily on first
/// use. Unlike <see cref="IJudgeResolver"/> — which only ever backfills a pre-existing invited
/// row — no Organizer row can exist before an organizer's first authenticated action, since
/// organizers self-register in Keycloak (registrationAllowed) rather than being provisioned ahead
/// of time. Takes primitives rather than <see cref="ICurrentUser"/> for the same reason
/// <see cref="JudgeResolver"/> does: <see cref="CurrentUser"/> would otherwise need to depend on
/// this, creating a DI cycle.
/// </summary>
public interface IOrganizerResolver
{
    Task<Organizer> ResolveOrCreateAsync(
        string sub, string? email, string? givenName, string? familyName, CancellationToken ct = default);
}

public sealed class OrganizerResolver(AppDbContext db) : IOrganizerResolver
{
    public async Task<Organizer> ResolveOrCreateAsync(
        string sub, string? email, string? givenName, string? familyName, CancellationToken ct = default)
    {
        var organizer = await db.Organizers.FirstOrDefaultAsync(o => o.KeycloakUserId == sub, ct);
        if (organizer is not null)
        {
            return organizer;
        }

        // Keycloak's `sub` for a given person can change independently of the Organizer row here
        // (realm re-import, the dev container losing its store, an admin recreating the account) —
        // Email is the stable link across that. Without this fallback, a returning organizer whose
        // sub no longer matches hits the unique index on Email on the insert below and every write
        // (including creating another competition) fails with a DbUpdateException.
        if (!string.IsNullOrWhiteSpace(email))
        {
            organizer = await db.Organizers.FirstOrDefaultAsync(o => o.Email == email, ct);
            if (organizer is not null)
            {
                organizer.KeycloakUserId = sub;
                await db.SaveChangesAsync(ct);
                return organizer;
            }
        }

        organizer = new Organizer
        {
            KeycloakUserId = sub,
            // Keycloak always issues an `email` claim for real users (birrapoint-realm.json
            // requires it), but fall back to a sub-derived placeholder rather than throwing —
            // defensive against test/service tokens minted without one.
            Email = string.IsNullOrWhiteSpace(email) ? $"{sub}@unknown.birrapoint.local" : email,
            FirstName = string.IsNullOrWhiteSpace(givenName) ? "Organizer" : givenName,
            LastName = string.IsNullOrWhiteSpace(familyName) ? sub : familyName,
        };

        db.Organizers.Add(organizer);
        await db.SaveChangesAsync(ct);

        return organizer;
    }
}
