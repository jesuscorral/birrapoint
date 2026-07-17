using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace BirraPoint.Api.Common.Auth;

public static class AuthenticationExtensions
{
    /// <summary>
    /// JWT bearer auth against Keycloak (T011): deny-by-default fallback policy (Principle VII —
    /// every endpoint requires authentication unless explicitly marked AllowAnonymous), plus
    /// ORGANIZER/JUDGE role policies backed by <see cref="KeycloakRolesClaimsTransformation"/>.
    /// </summary>
    public static IServiceCollection AddKeycloakAuthentication(
        this IServiceCollection services, IConfiguration configuration, bool isDevelopment)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddSingleton<IClaimsTransformation, KeycloakRolesClaimsTransformation>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Keycloak:Authority"];
                // Local Keycloak runs over plain HTTP; production sits behind HTTPS ingress (R-19).
                options.RequireHttpsMetadata = !isDevelopment;
                // Keep Keycloak's raw claim names (sub, email, realm_access) instead of the legacy
                // XML/SOAP claim-type remapping JwtBearer applies by default.
                options.MapInboundClaims = false;
                // birrapoint-spa carries an oidc-audience-mapper protocol mapper (infra/keycloak/
                // birrapoint-realm.json) stamping this value onto every access token (ADR-0009).
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidAudience = configuration["Keycloak:ApiAudience"];
                options.Events = new JwtBearerEvents
                {
                    // Browser WebSocket handshakes can't set an Authorization header, so
                    // CompetitionHub's clients authenticate via ?access_token= on the handshake
                    // (contracts/signalr-hub.md, standard SignalR-JWT pattern). Scoped to the hub
                    // path only — every other endpoint still requires the Authorization header.
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) &&
                            context.HttpContext.Request.Path.StartsWithSegments("/hubs/competition"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy("ORGANIZER", policy => policy.RequireRole("ORGANIZER"))
            .AddPolicy("JUDGE", policy => policy.RequireRole("JUDGE"));

        return services;
    }
}
