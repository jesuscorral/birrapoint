namespace BirraPoint.Api.Common.Auth;

/// <summary>Claims accessor for the authenticated caller of the current request.</summary>
public interface ICurrentUser
{
    /// <summary>Keycloak subject (JWT `sub`).</summary>
    string Sub { get; }

    string? Email { get; }

    IReadOnlyList<string> Roles { get; }
}
