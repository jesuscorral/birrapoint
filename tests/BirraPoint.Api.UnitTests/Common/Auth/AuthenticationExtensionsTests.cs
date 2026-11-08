using System.Security.Claims;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Modules.Organization.Identity;
using BuildingBlocks.Identity;
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
                ["Keycloak:ApiAudience"] = "birrapoint-api",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeycloakAuthentication(configuration, isDevelopment: true);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RegistersTheJwtBearerScheme()
    {
        await using var provider = BuildProvider();
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        var scheme = await schemeProvider.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme);

        Assert.NotNull(scheme);
    }

    [Fact]
    public async Task RegistersOrganizerAndJudgeRolePolicies()
    {
        await using var provider = BuildProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        Assert.NotNull(await policyProvider.GetPolicyAsync("ORGANIZER"));
        Assert.NotNull(await policyProvider.GetPolicyAsync("JUDGE"));
    }

    [Fact]
    public async Task FallbackPolicyRequiresAnAuthenticatedUser()
    {
        await using var provider = BuildProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await policyProvider.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task RegistersTheKeycloakClaimsTransformation()
    {
        using var provider = BuildProvider();

        var transformation = provider.GetRequiredService<IClaimsTransformation>();
        var identity = new ClaimsIdentity([new Claim("realm_access", """{"roles":["JUDGE"]}""")], "test");

        var principal = await transformation.TransformAsync(new ClaimsPrincipal(identity));

        Assert.True(principal.IsInRole("JUDGE"));
    }

    [Fact]
    public void RegistersCurrentUserAndItsHttpContextAccessorDependency()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IHttpContextAccessor>());
        Assert.NotNull(provider.GetRequiredService<ICurrentUser>());
    }

    [Fact]
    public void DoesNotRegisterTheOrganizationResolvers()
    {
        using var provider = BuildProvider();

        // Judge/organizer resolution belongs to AddOrganizationModule, not to the Keycloak authentication wiring.
        Assert.Null(provider.GetService<IJudgeResolver>());
        Assert.Null(provider.GetService<IOrganizerResolver>());
    }
}
