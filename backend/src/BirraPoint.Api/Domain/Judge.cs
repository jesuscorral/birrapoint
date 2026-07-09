namespace BirraPoint.Api.Domain;

/// <summary>Competition-scoped judge profile; identity lives in Keycloak (Principle VII).</summary>
public class Judge : Entity
{
    public Guid CompetitionId { get; set; }

    /// <summary>COI matching key vs Participant.Email + EntryCollaborator.Email; unique per competition.</summary>
    public required string Email { get; set; }

    /// <summary>Set once provisioned in Keycloak (R-10).</summary>
    public string? KeycloakUserId { get; set; }

    /// <summary>Defaults to the email local-part until first login.</summary>
    public required string DisplayName { get; set; }
}
