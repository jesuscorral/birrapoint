using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Features.Import;

/// <summary>
/// One parsed row of a staged <see cref="ImportBatch"/> (contracts/import-file.md). Resolved via
/// the assign-style/exclude actions (FR-011) before the batch can be consolidated.
/// </summary>
public class ImportRow : Entity
{
    public Guid ImportBatchId { get; set; }

    /// <summary>1-based position among the file's data rows (excludes the header row).</summary>
    public int RowNumber { get; set; }

    public ImportRowStatus Status { get; set; }

    public string? ParticipantName { get; set; }

    public string? ParticipantEmail { get; set; }

    public string? BeerName { get; set; }

    /// <summary>Raw cell text as read from the file — may not match any catalog style (StyleMismatch).</summary>
    public string? StyleText { get; set; }

    /// <summary>Collaborator emails as parsed from the semicolon-separated cell, stored as a JSON array (jsonb).</summary>
    public required string CollaboratorsJson { get; set; }

    /// <summary>Set at parse time when Style matched the catalog, or by the organizer via assign-style.</summary>
    public string? ResolvedStyleCode { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>
/// <see cref="Valid"/>/<see cref="StyleMismatch"/>/<see cref="Invalid"/> are the parse-time
/// outcomes (import-file.md); <see cref="Excluded"/> is a resolution outcome set only via the
/// `exclude` action and — like Valid — never blocks consolidation (FR-011).
/// </summary>
public enum ImportRowStatus
{
    Valid,
    StyleMismatch,
    Invalid,
    Excluded,
}
