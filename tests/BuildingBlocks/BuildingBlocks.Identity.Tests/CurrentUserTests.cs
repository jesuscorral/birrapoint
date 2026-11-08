using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Identity.Tests;

public sealed class CurrentUserTests
{
    [Fact]
    public void ReadsSubEmailNameAndRolesFromTheCurrentPrincipal()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", "kc-user-123"),
            new Claim("email", "judge@example.test"),
            new Claim("name", "Judge Judy"),
            new Claim("given_name", "Judge"),
            new Claim("family_name", "Judy"),
            new Claim(ClaimTypes.Role, "ORGANIZER"),
            new Claim(ClaimTypes.Role, "JUDGE"),
        ], "test");
        var currentUser = CurrentUserFor(new ClaimsPrincipal(identity));

        Assert.Equal("kc-user-123", currentUser.Sub);
        Assert.Equal("judge@example.test", currentUser.Email);
        Assert.Equal("Judge Judy", currentUser.Name);
        Assert.Equal("Judge", currentUser.GivenName);
        Assert.Equal("Judy", currentUser.FamilyName);
        Assert.Equal(["ORGANIZER", "JUDGE"], currentUser.Roles);
    }

    [Fact]
    public void EmailIsNullWhenTheClaimIsAbsent()
    {
        var identity = new ClaimsIdentity([new Claim("sub", "kc-user-123")], "test");
        var currentUser = CurrentUserFor(new ClaimsPrincipal(identity));

        Assert.Null(currentUser.Email);
    }

    [Fact]
    public void NameIsNullWhenTheClaimIsAbsent()
    {
        var identity = new ClaimsIdentity([new Claim("sub", "kc-user-123")], "test");
        var currentUser = CurrentUserFor(new ClaimsPrincipal(identity));

        Assert.Null(currentUser.Name);
    }

    [Fact]
    public void SubThrowsWhenTheClaimIsMissing()
    {
        var identity = new ClaimsIdentity([new Claim("email", "judge@example.test")], "test");
        var currentUser = CurrentUserFor(new ClaimsPrincipal(identity));

        Assert.Throws<InvalidOperationException>(() => currentUser.Sub);
    }

    [Fact]
    public void ThrowsWhenThereIsNoHttpContext()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var currentUser = new CurrentUser(accessor);

        Assert.Throws<InvalidOperationException>(() => currentUser.Sub);
    }

    private static CurrentUser CurrentUserFor(ClaimsPrincipal principal)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        return new CurrentUser(accessor);
    }
}
