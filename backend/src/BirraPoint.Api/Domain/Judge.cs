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

    /// <summary>Free text, club vocabulary (e.g. "Certificado", "Reconocido", "Pendiente de
    /// Rango") — not a controlled catalog. Null for judges created via the plain email-list flow
    /// (US14/FR-057).</summary>
    public string? BjcpRank { get; set; }

    /// <summary>Stored verbatim; observed formats include `E####`, bare numeric, and the
    /// placeholder `Pte` ("not yet assigned") — never parsed or validated (US14/FR-057).</summary>
    public string? BjcpId { get; set; }

    /// <summary>Free text; informational only, not cross-checked against this competition's own
    /// CompetitionCategory names (US14/FR-057, spec.md Assumptions).</summary>
    public string? PreferredCategory { get; set; }

    /// <summary>Free-text notes (table-mate requests, availability, aversions); rendered as plain
    /// text only — never interpreted as markup, even when the source cell contains literal
    /// `&lt;br&gt;`-style text (US14/FR-057/R-20).</summary>
    public string? Preferences { get; set; }
}
