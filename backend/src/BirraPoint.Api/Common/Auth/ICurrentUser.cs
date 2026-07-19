using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Common.Auth;

/// <summary>Claims accessor for the authenticated caller of the current request.</summary>
public interface ICurrentUser
{
    /// <summary>Keycloak subject (JWT `sub`).</summary>
    string Sub { get; }

    string? Email { get; }

    string? Name { get; }

    IReadOnlyList<string> Roles { get; }

    /// <summary>Every Judge row across competitions matching this caller's email, backfilling
    /// KeycloakUserId/DisplayName on first login (see <see cref="IJudgeResolver"/>).</summary>
    Task<IReadOnlyList<Judge>> GetJudgeRecordsAsync(CancellationToken ct = default);
}
