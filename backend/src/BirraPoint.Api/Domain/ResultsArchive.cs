namespace BirraPoint.Api.Domain;

/// <summary>Bundled results ZIP for a competition (T074/FR-040), upserted by CompetitionId on regeneration.</summary>
public class ResultsArchive : Entity
{
    public Guid CompetitionId { get; set; }

    public required byte[] ZipBytes { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
}
