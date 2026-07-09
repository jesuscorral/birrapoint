namespace BirraPoint.Api.Domain;

public class Competition : Entity
{
    public required string Name { get; set; }

    public required string Venue { get; set; }

    public DateOnly StartDate { get; set; }

    /// <summary>Must be >= StartDate (DB check constraint).</summary>
    public DateOnly EndDate { get; set; }

    public string? Description { get; set; }

    public string? LogoUrl { get; set; }

    public int? EntryLimit { get; set; }

    public DateOnly? StartRegistration { get; set; }

    /// <summary>Must be >= StartRegistration when both set (DB check constraint).</summary>
    public DateOnly? EndRegistration { get; set; }

    public CompetitionState State { get; set; } = CompetitionState.Draft;

    /// <summary>Keycloak subject of the organizer.</summary>
    public required string CreatedByUserId { get; set; }
}
