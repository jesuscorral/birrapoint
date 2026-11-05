using System.Security.Claims;
using BirraPoint.Api.Common.Auth;

namespace BirraPoint.Api.UnitTests.Common.Auth;

public sealed class KeycloakRolesClaimsTransformationTests
{
    private readonly KeycloakRolesClaimsTransformation _transformation = new();

    [Fact]
    public async Task MapsRealmAccessRolesToRoleClaims()
    {
        var principal = AuthenticatedPrincipal("""{"roles":["ORGANIZER","JUDGE"]}""");

        var result = await _transformation.TransformAsync(principal);

        Assert.True(result.IsInRole("ORGANIZER"));
        Assert.True(result.IsInRole("JUDGE"));
    }

    [Fact]
    public async Task IsANoopWhenRealmAccessClaimIsAbsent()
    {
        var identity = new ClaimsIdentity([new Claim("sub", "user-1")], "test");
        var principal = new ClaimsPrincipal(identity);

        var result = await _transformation.TransformAsync(principal);

        Assert.Empty(result.FindAll(ClaimTypes.Role));
    }

    [Fact]
    public async Task DoesNotDuplicateRoleClaimsWhenRunMoreThanOnce()
    {
        var principal = AuthenticatedPrincipal("""{"roles":["ORGANIZER"]}""");

        var once = await _transformation.TransformAsync(principal);
        var twice = await _transformation.TransformAsync(once);

        Assert.Single(twice.FindAll(ClaimTypes.Role));
    }

    [Fact]
    public async Task IsANoopForAnUnauthenticatedPrincipal()
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
