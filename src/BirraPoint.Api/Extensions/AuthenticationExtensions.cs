using BuildingBlocks.Identity;
using BuildingBlocks.Keycloak.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace BirraPoint.Api.Extensions;

internal static class AuthenticationExtensions
{
    /// <summary>
    /// JWT bearer auth against Keycloak: deny-by-default fallback policy (Principle VII —
    /// every endpoint requires authentication unless explicitly marked AllowAnonymous), plus
    /// ORGANIZER/JUDGE role policies backed by the realm-roles claims transformation of <c>BuildingBlocks.Keycloak.Authentication</c>.
    /// </summary>
    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.AddCurrentUser();

        // CompetitionHub's browser WebSocket handshakes can't set an Authorization header, so the
        // hub path alone accepts ?access_token= (contracts/signalr-hub.md); every other endpoint
        // still requires the header. The audience is stamped by the birrapoint-spa audience mapper
        // (ADR-0009); local Keycloak runs over plain HTTP, production sits behind HTTPS ingress (R-19).
        services.AddKeycloakJwtBearer(options =>
        {
            options.Authority = configuration["Keycloak:Authority"];
            options.Audience = configuration["Keycloak:ApiAudience"];
            options.RequireHttpsMetadata = !isDevelopment;
            options.TokenQueryPaths.Add("/hubs/competition");
        });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy("ORGANIZER", policy => policy.RequireRole("ORGANIZER"))
            .AddPolicy("JUDGE", policy => policy.RequireRole("JUDGE"));

        return services;
    }
}
