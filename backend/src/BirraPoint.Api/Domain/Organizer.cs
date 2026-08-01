namespace BirraPoint.Api.Domain;

/// <summary>
/// Account-level profile for a competition organizer; identity lives in Keycloak (Principle VII).
/// Lazily resolved/created on first authenticated action via <see cref="Auth.IOrganizerResolver"/>
/// — unlike <see cref="Judge"/>, no row can pre-exist before that first login, since organizers are
/// self-registered rather than invited. One Organizer owns many <see cref="Competition"/> rows.
/// </summary>
public class Organizer : Entity
{
    /// <summary>Keycloak subject (JWT `sub`); set at resolution time, never null once created.</summary>
    public required string KeycloakUserId { get; set; }

    public required string Email { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }
}
