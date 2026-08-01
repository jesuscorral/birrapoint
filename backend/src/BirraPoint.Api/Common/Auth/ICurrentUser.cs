using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Common.Auth;

/// <summary>Claims accessor for the authenticated caller of the current request.</summary>
public interface ICurrentUser
{
    /// <summary>Keycloak subject (JWT `sub`).</summary>
    string Sub { get; }

    string? Email { get; }

    string? Name { get; }

    /// <summary>JWT `given_name` claim; used to populate Organizer.FirstName without splitting
    /// <see cref="Name"/> (which may be a display name in any word order/script).</summary>
    string? GivenName { get; }

    /// <summary>JWT `family_name` claim; used to populate Organizer.LastName.</summary>
    string? FamilyName { get; }

    IReadOnlyList<string> Roles { get; }

    /// <summary>Every Judge row across competitions matching this caller's email, backfilling
    /// KeycloakUserId/DisplayName on first login (see <see cref="IJudgeResolver"/>).</summary>
    Task<IReadOnlyList<Judge>> GetJudgeRecordsAsync(CancellationToken ct = default);

    /// <summary>This caller's Organizer row, created lazily on first call (see
    /// <see cref="IOrganizerResolver"/>).</summary>
    Task<Organizer> GetOrganizerAsync(CancellationToken ct = default);
}
