using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Features.Import;

/// <summary>
/// One parsed row of a staged <see cref="ImportBatch"/> (contracts/import-file.md). Resolved via
/// the full-row-edit PUT or the exclude action (FR-011) before the batch can be consolidated.
/// Mirrors the ACCE `.xlsx` column set 1:1 so the review screen operates purely off staged data.
/// </summary>
public class ImportRow : Entity
{
    public Guid ImportBatchId { get; set; }

    /// <summary>1-based position among the file's data rows (excludes the header row).</summary>
    public int RowNumber { get; set; }

    public ImportRowStatus Status { get; set; }

    public string? ParticipantName { get; set; }

    public string? ParticipantEmail { get; set; }

    public string? AcceMemberNumberText { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? Phone { get; set; }

    /// <summary>Raw "Categoria" cell text — may not match any of this competition's CompetitionCategory names.</summary>
    public string? CategoryText { get; set; }

    /// <summary>Set at parse time when CategoryText matched a competition category, or by the organizer via the row edit.</summary>
    public Guid? ResolvedCompetitionCategoryId { get; set; }

    /// <summary>Raw "Estilo" cell text as read from the file — may not match any catalog style (StyleMismatch).</summary>
    public string? StyleText { get; set; }

    /// <summary>Set at parse time when Estilo matched the catalog, or by the organizer via the row edit.</summary>
    public string? ResolvedStyleCode { get; set; }

    /// <summary>"Marca temporal".</summary>
    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>"Grado alcohol: (%)".</summary>
    public decimal? AbvPercent { get; set; }

    public DateOnly? BrewDate { get; set; }

    public DateOnly? BottlingDate { get; set; }

    public string? Malts { get; set; }

    public string? Hops { get; set; }

    public string? Yeast { get; set; }

    public string? OtherIngredients { get; set; }

    public string? EntryInstructions { get; set; }

    /// <summary>Always null coming out of import (the ACCE format has no beer-name column); purely organizer-editable.</summary>
    public string? BeerName { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>
/// <see cref="Valid"/>/<see cref="StyleMismatch"/>/<see cref="CategoryMismatch"/>/
/// <see cref="CategoryStyleMismatch"/>/<see cref="Invalid"/> are the parse-time outcomes
/// (import-file.md); <see cref="Excluded"/> is a resolution outcome set only via the exclude
/// action and — like <see cref="Valid"/> — never blocks consolidation (FR-011).
/// </summary>
public enum ImportRowStatus
{
    Valid,
    StyleMismatch,
    CategoryMismatch,

    /// <summary>Category and style each individually resolve, but the style isn't assigned to
    /// that category under this competition's FR-052 configuration (FR-053).</summary>
    CategoryStyleMismatch,
    Invalid,
    Excluded,
}
