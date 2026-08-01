using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.IntegrationTests.Auth;

/// <summary>
/// OrganizerResolver creates the caller's Organizer row lazily on first use — unlike
/// JudgeResolver, no row can pre-exist before an organizer's first authenticated action, since
/// organizers self-register in Keycloak rather than being invited ahead of time.
/// </summary>
public sealed class OrganizerResolverTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private AppDbContext NewContext() => new(fixture.Options);

    [Fact]
    public async Task Creates_an_organizer_row_on_first_resolution()
    {
        var sub = $"kc-organizer-{Guid.NewGuid():N}";
        var email = $"organizer-{Guid.NewGuid():N}@example.test";

        await using var db = NewContext();
        var resolver = new OrganizerResolver(db);

        var organizer = await resolver.ResolveOrCreateAsync(sub, email, "Ada", "Lovelace");

        Assert.Equal(sub, organizer.KeycloakUserId);
        Assert.Equal(email, organizer.Email);
        Assert.Equal("Ada", organizer.FirstName);
        Assert.Equal("Lovelace", organizer.LastName);

        await using var verify = NewContext();
        var stored = await verify.Organizers.AsNoTracking().SingleAsync(o => o.KeycloakUserId == sub);
        Assert.Equal(email, stored.Email);
    }

    [Fact]
    public async Task Replay_returns_the_same_row_without_creating_a_duplicate()
    {
        var sub = $"kc-organizer-{Guid.NewGuid():N}";
        var email = $"organizer-{Guid.NewGuid():N}@example.test";

        await using (var first = NewContext())
        {
            await new OrganizerResolver(first).ResolveOrCreateAsync(sub, email, "Ada", "Lovelace");
        }

        await using var db = NewContext();
        // Different claim values on replay must not overwrite the already-created row.
        var resolved = await new OrganizerResolver(db).ResolveOrCreateAsync(sub, "other@example.test", "Other", "Name");

        Assert.Equal(email, resolved.Email);
        Assert.Equal("Ada", resolved.FirstName);

        await using var verify = NewContext();
        var count = await verify.Organizers.AsNoTracking().CountAsync(o => o.KeycloakUserId == sub);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Relinks_the_existing_row_when_the_same_email_resolves_under_a_new_sub()
    {
        var originalSub = $"kc-organizer-{Guid.NewGuid():N}";
        var newSub = $"kc-organizer-{Guid.NewGuid():N}";
        var email = $"organizer-{Guid.NewGuid():N}@example.test";

        Guid organizerId;
        await using (var first = NewContext())
        {
            var created = await new OrganizerResolver(first)
                .ResolveOrCreateAsync(originalSub, email, "Ada", "Lovelace");
            organizerId = created.Id;
        }

        // Same email, but the caller's token now carries a different `sub` (e.g. the Keycloak
        // identity was recreated) — must re-link the existing row instead of attempting a second
        // insert with the same email, which would violate the unique index on Email.
        await using var db = NewContext();
        var resolved = await new OrganizerResolver(db).ResolveOrCreateAsync(newSub, email, "Ada", "Lovelace");

        Assert.Equal(organizerId, resolved.Id);
        Assert.Equal(newSub, resolved.KeycloakUserId);

        await using var verify = NewContext();
        Assert.Equal(1, await verify.Organizers.AsNoTracking().CountAsync(o => o.Email == email));
        var stored = await verify.Organizers.AsNoTracking().SingleAsync(o => o.Email == email);
        Assert.Equal(newSub, stored.KeycloakUserId);
    }

    [Fact]
    public async Task Falls_back_to_placeholder_names_when_given_or_family_name_claims_are_absent()
    {
        var sub = $"kc-organizer-{Guid.NewGuid():N}";
        var email = $"organizer-{Guid.NewGuid():N}@example.test";

        await using var db = NewContext();
        var organizer = await new OrganizerResolver(db).ResolveOrCreateAsync(sub, email, givenName: null, familyName: null);

        Assert.Equal("Organizer", organizer.FirstName);
        Assert.Equal(sub, organizer.LastName);
    }
}
