using System.Security.Claims;
using BirraPoint.Api.Common.Auth;
using Microsoft.AspNetCore.Http;

namespace BirraPoint.Api.UnitTests.Common.Auth;

public sealed class CurrentUserTests
{
    [Fact]
    public void Reads_sub_email_and_roles_from_the_current_principal()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", "kc-user-123"),
            new Claim("email", "judge@example.test"),
            new Claim(ClaimTypes.Role, "ORGANIZER"),
            new Claim(ClaimTypes.Role, "JUDGE"),
        ], "test");
        var currentUser = CurrentUserFor(new ClaimsPrincipal(identity));

        Assert.Equal("kc-user-123", currentUser.Sub);
        Assert.Equal("judge@example.test", currentUser.Email);
        Assert.Equal(["ORGANIZER", "JUDGE"], currentUser.Roles);
    }

    [Fact]
    public void Email_is_null_when_the_claim_is_absent()
    {
        var identity = new ClaimsIdentity([new Claim("sub", "kc-user-123")], "test");
        var currentUser = CurrentUserFor(new ClaimsPrincipal(identity));

        Assert.Null(currentUser.Email);
    }

    [Fact]
    public void Sub_throws_when_the_claim_is_missing()
    {
        var identity = new ClaimsIdentity([new Claim("email", "judge@example.test")], "test");
        var currentUser = CurrentUserFor(new ClaimsPrincipal(identity));

        Assert.Throws<InvalidOperationException>(() => currentUser.Sub);
    }

    [Fact]
    public void Throws_when_there_is_no_http_context()
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
