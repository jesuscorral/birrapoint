using System.Security.Claims;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Domain;
using Microsoft.AspNetCore.Http;

namespace BirraPoint.Api.UnitTests.Common.Auth;

public sealed class CurrentUserTests
{
    private sealed class SpyJudgeResolver : IJudgeResolver
    {
        public (string Sub, string? Email, string? Name)? LastCall { get; private set; }

        public Task<IReadOnlyList<Judge>> ResolveAndBackfillAsync(
            string sub, string? email, string? name, CancellationToken ct = default)
        {
            LastCall = (sub, email, name);
            return Task.FromResult<IReadOnlyList<Judge>>([]);
        }
    }

    private sealed class SpyOrganizerResolver : IOrganizerResolver
    {
        public (string Sub, string? Email, string? GivenName, string? FamilyName)? LastCall { get; private set; }

        public Task<Organizer> ResolveOrCreateAsync(
            string sub, string? email, string? givenName, string? familyName, CancellationToken ct = default)
        {
            LastCall = (sub, email, givenName, familyName);
            return Task.FromResult(new Organizer
            {
                KeycloakUserId = sub,
                Email = email ?? "unused@example.test",
                FirstName = givenName ?? "Unused",
                LastName = familyName ?? "Unused",
            });
        }
    }

    [Fact]
    public void Reads_sub_email_name_and_roles_from_the_current_principal()
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
    public void Email_is_null_when_the_claim_is_absent()
    {
        var identity = new ClaimsIdentity([new Claim("sub", "kc-user-123")], "test");
        var currentUser = CurrentUserFor(new ClaimsPrincipal(identity));

        Assert.Null(currentUser.Email);
    }

    [Fact]
    public void Name_is_null_when_the_claim_is_absent()
    {
        var identity = new ClaimsIdentity([new Claim("sub", "kc-user-123")], "test");
        var currentUser = CurrentUserFor(new ClaimsPrincipal(identity));

        Assert.Null(currentUser.Name);
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
        var currentUser = new CurrentUser(accessor, new SpyJudgeResolver(), new SpyOrganizerResolver());

        Assert.Throws<InvalidOperationException>(() => currentUser.Sub);
    }

    [Fact]
    public async Task GetJudgeRecordsAsync_delegates_to_the_resolver_with_sub_email_and_name()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", "kc-user-123"),
            new Claim("email", "judge@example.test"),
            new Claim("name", "Judge Judy"),
        ], "test");
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        var resolver = new SpyJudgeResolver();
        var currentUser = new CurrentUser(accessor, resolver, new SpyOrganizerResolver());

        await currentUser.GetJudgeRecordsAsync();

        Assert.Equal(("kc-user-123", "judge@example.test", "Judge Judy"), resolver.LastCall);
    }

    [Fact]
    public async Task GetOrganizerAsync_delegates_to_the_resolver_with_sub_email_given_and_family_name()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", "kc-user-123"),
            new Claim("email", "organizer@example.test"),
            new Claim("given_name", "Ada"),
            new Claim("family_name", "Lovelace"),
        ], "test");
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
        var resolver = new SpyOrganizerResolver();
        var currentUser = new CurrentUser(accessor, new SpyJudgeResolver(), resolver);

        await currentUser.GetOrganizerAsync();

        Assert.Equal(("kc-user-123", "organizer@example.test", "Ada", "Lovelace"), resolver.LastCall);
    }

    private static CurrentUser CurrentUserFor(ClaimsPrincipal principal)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        return new CurrentUser(accessor, new SpyJudgeResolver(), new SpyOrganizerResolver());
    }
}
