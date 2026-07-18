using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace BirraPoint.Api.IntegrationTests.TestHost;

/// <summary>
/// T018: mints JWTs shaped like Keycloak's without a real IdP round-trip. Signs with a fixed
/// test-only key so <see cref="ApiFactory"/> can validate tokens statically (no metadata fetch);
/// the `realm_access` claim matches the shape <c>KeycloakRolesClaimsTransformation</c> parses.
/// </summary>
public static class TestJwtIssuer
{
    public const string Audience = "birrapoint-api";
    private const string Issuer = "https://test-issuer.birrapoint.local/realms/birrapoint";

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("T018-integration-test-only-signing-key-not-for-any-real-use!!"));

    public static TokenValidationParameters ValidationParameters { get; } = new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = SigningKey,
    };

    public static string IssueToken(string sub, string? email = null, params string[] roles)
    {
        var claims = new List<Claim> { new("sub", sub) };
        if (email is not null)
        {
            claims.Add(new Claim("email", email));
        }

        var realmAccessJson = JsonSerializer.Serialize(new { roles });
        claims.Add(new Claim("realm_access", realmAccessJson, JsonClaimValueTypes.Json));

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
