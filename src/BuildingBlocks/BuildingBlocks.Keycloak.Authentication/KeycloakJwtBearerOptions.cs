using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Keycloak.Authentication;

/// <summary>Settings for <see cref="KeycloakAuthenticationExtensions.AddKeycloakJwtBearer"/>.</summary>
public sealed class KeycloakJwtBearerOptions
{
    /// <summary>The realm's issuer URL (<c>https://host/realms/name</c>); the OIDC metadata is discovered from it.</summary>
    public string? Authority { get; set; }

    /// <summary>The audience every access token must carry (<c>aud</c>); validation is always on.</summary>
    public string? Audience { get; set; }

    /// <summary>Whether the metadata endpoint must use HTTPS. Turn off only for a local plain-HTTP Keycloak.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Request path prefixes on which the token may arrive as <c>?access_token=</c> instead of the
    /// <c>Authorization</c> header (browser WebSocket handshakes cannot set headers). Empty means
    /// the query token is never accepted.
    /// </summary>
    public IList<PathString> TokenQueryPaths { get; } = [];

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Authority))
        {
            throw new InvalidOperationException($"{nameof(KeycloakJwtBearerOptions)}.{nameof(Authority)} is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException($"{nameof(KeycloakJwtBearerOptions)}.{nameof(Audience)} is not configured.");
        }
    }
}
