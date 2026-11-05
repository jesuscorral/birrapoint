using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Modules.Publication.Domain;

/// <summary>Bundled results ZIP for a competition (FR-040), upserted by CompetitionId on regeneration.</summary>
internal class ResultsArchive : Entity
{
    public Guid CompetitionId { get; set; }

    public required byte[] ZipBytes { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
}
