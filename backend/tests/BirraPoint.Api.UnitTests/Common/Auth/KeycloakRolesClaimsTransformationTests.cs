using System.Security.Claims;
using BirraPoint.Api.Common.Auth;

namespace BirraPoint.Api.UnitTests.Common.Auth;

public sealed class KeycloakRolesClaimsTransformationTests
{
    private readonly KeycloakRolesClaimsTransformation _transformation = new();

    [Fact]
    public async Task Maps_realm_access_roles_to_role_claims()
    {
        var principal = AuthenticatedPrincipal("""{"roles":["ORGANIZER","JUDGE"]}""");

        var result = await _transformation.TransformAsync(principal);

        Assert.True(result.IsInRole("ORGANIZER"));
        Assert.True(result.IsInRole("JUDGE"));
    }

    [Fact]
    public async Task Is_a_noop_when_realm_access_claim_is_absent()
    {
        var identity = new ClaimsIdentity([new Claim("sub", "user-1")], "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await _transformation.TransformAsync(principal);

        Assert.Empty(result.FindAll(ClaimTypes.Role));
    }

    [Fact]
    public async Task Does_not_duplicate_role_claims_when_run_more_than_once()
    {
        var principal = AuthenticatedPrincipal("""{"roles":["ORGANIZER"]}""");

        var once = await _transformation.TransformAsync(principal);
        var twice = await _transformation.TransformAsync(once);

        Assert.Single(twice.FindAll(ClaimTypes.Role));
    }

    [Fact]
    public async Task Is_a_noop_for_an_unauthenticated_principal()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await _transformation.TransformAsync(principal);

        Assert.Empty(result.FindAll(ClaimTypes.Role));
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(string realmAccessJson)
    {
        var identity = new ClaimsIdentity(
            [new Claim("sub", "user-1"), new Claim("realm_access", realmAccessJson)],
            authenticationType: "test");
        return new ClaimsPrincipal(identity);
    }
}
