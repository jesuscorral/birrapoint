using BirraPoint.Api.Common.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.UnitTests.Common.Auth;

/// <summary>DI-wiring smoke test — no network calls (JwtBearer only fetches metadata at request time).</summary>
public sealed class AuthenticationExtensionsTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = "https://fake-issuer.test/realms/birrapoint",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeycloakAuthentication(configuration, isDevelopment: true);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Registers_the_jwt_bearer_scheme()
    {
        await using var provider = BuildProvider();
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        var scheme = await schemeProvider.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme);

        Assert.NotNull(scheme);
    }

    [Fact]
    public async Task Registers_organizer_and_judge_role_policies()
    {
        await using var provider = BuildProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.NotNull(await policyProvider.GetPolicyAsync("ORGANIZER"));
        Assert.NotNull(await policyProvider.GetPolicyAsync("JUDGE"));
    }

    [Fact]
    public async Task Fallback_policy_requires_an_authenticated_user()
    {
        await using var provider = BuildProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await policyProvider.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback!.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public void Registers_the_keycloak_claims_transformation()
    {
        using var provider = BuildProvider();

        var transformation = provider.GetRequiredService<IClaimsTransformation>();

        Assert.IsType<KeycloakRolesClaimsTransformation>(transformation);
    }

    [Fact]
    public void Registers_current_user_and_its_http_context_accessor_dependency()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IHttpContextAccessor>());
        Assert.IsType<CurrentUser>(provider.GetRequiredService<ICurrentUser>());
    }
}
