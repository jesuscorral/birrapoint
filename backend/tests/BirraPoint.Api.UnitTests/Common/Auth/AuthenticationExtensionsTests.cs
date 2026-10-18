using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
        // JudgeResolver (behind ICurrentUser) depends on AppDbContext; registered here purely to
        // satisfy the DI graph — this test never opens a connection or issues a query.
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql("Host=localhost;Database=birrapoint_di_wiring_test;Username=test;Password=test"));
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
    public void RegistersTheKeycloakClaimsTransformation()
    {
        using var provider = BuildProvider();

        var transformation = provider.GetRequiredService<IClaimsTransformation>();

        Assert.IsType<KeycloakRolesClaimsTransformation>(transformation);
    }

    [Fact]
    public void RegistersCurrentUserAndItsHttpContextAccessorDependency()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IHttpContextAccessor>());
        Assert.IsType<CurrentUser>(provider.GetRequiredService<ICurrentUser>());
    }
}
