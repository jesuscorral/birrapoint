using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace BirraPoint.Api.Common.Auth;

/// <summary>
/// Keycloak puts realm roles under a nested `realm_access.roles` claim, not flat role claims —
/// ASP.NET Core's [Authorize(Roles=...)] / User.IsInRole() need them as individual
/// <see cref="ClaimTypes.Role"/> claims instead. ASP.NET Core may invoke a claims transformation
/// more than once per request, so this guards against adding duplicate role claims.
/// </summary>
public sealed class KeycloakRolesClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
        {
            return Task.FromResult(principal);
        }

        if (identity.HasClaim(claim => claim.Type == ClaimTypes.Role))
        {
            return Task.FromResult(principal);
        }

        var realmAccessJson = identity.FindFirst("realm_access")?.Value;
        if (realmAccessJson is null)
        {
            return Task.FromResult(principal);
        }

        using var document = JsonDocument.Parse(realmAccessJson);
        if (document.RootElement.TryGetProperty("roles", out var roles))
        {
            foreach (var role in roles.EnumerateArray())
            {
                var roleName = role.GetString();
                if (!string.IsNullOrEmpty(roleName))
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
                }
            }
        }

        return Task.FromResult(principal);
    }
}
