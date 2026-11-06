using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Keycloak.Authentication;

public static class KeycloakAuthenticationExtensions
{
    /// <summary>
    /// Registers JWT bearer authentication against a Keycloak realm and the transformation that
    /// turns <c>realm_access.roles</c> into role claims. Throws immediately when the authority or
    /// audience is blank, so a misconfigured host fails at startup rather than on the first request.
    /// </summary>
    public static AuthenticationBuilder AddKeycloakJwtBearer(
        this IServiceCollection services, Action<KeycloakJwtBearerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var settings = new KeycloakJwtBearerOptions();
        configure(settings);
        settings.Validate();

        services.AddSingleton<IClaimsTransformation, KeycloakRolesClaimsTransformation>();

        return services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = settings.Authority;
                options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
                // Keep Keycloak's raw claim names (sub, email, realm_access) instead of the legacy
                // XML/SOAP claim-type remapping JwtBearer applies by default.
                options.MapInboundClaims = false;
                options.TokenValidationParameters.ValidateAudience = true;
                options.TokenValidationParameters.ValidAudience = settings.Audience;

                var queryPaths = settings.TokenQueryPaths.ToArray();
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) &&
                            queryPaths.Any(path => context.HttpContext.Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase)))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                };
            });
    }
}
