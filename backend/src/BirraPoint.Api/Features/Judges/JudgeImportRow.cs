using BirraPoint.Api.Domain;

namespace BirraPoint.Api.Features.Judges;

/// <summary>
/// One parsed row of a staged <see cref="JudgeImportBatch"/> (contracts/judge-import-file.md).
/// Simpler than Features/Import/ImportRow.cs: no cell resolves against any catalog for a judge
/// roster, so there is no mismatch/unresolved-reference status — only whether the two required
/// fields (Name/Email) are present and well-formed (US14/FR-056).
/// </summary>
public class JudgeImportRow : Entity
{
    public Guid JudgeImportBatchId { get; set; }

    /// <summary>1-based position among the file's data rows (excludes the header row).</summary>
    public int RowNumber { get; set; }

    public JudgeImportRowStatus Status { get; set; }

    /// <summary>Raw parsed "Nombre y apellidos" cell, may be null/malformed when Status = Invalid.</summary>
    public string? Name { get; set; }

    /// <summary>Raw parsed "Correo electrónico" cell.</summary>
    public string? Email { get; set; }

    /// <summary>Raw parsed "Rango BJCP" cell — free text, club vocabulary, not a controlled catalog.</summary>
    public string? BjcpRank { get; set; }

    /// <summary>Raw parsed "BJCP ID" cell, stored verbatim (incl. the "Pte" placeholder).</summary>
    public string? BjcpId { get; set; }

    /// <summary>Raw parsed "Categoría preferida" cell — informational only.</summary>
    public string? PreferredCategory { get; set; }

    /// <summary>Raw parsed "Preferencias" cell, stored verbatim as plain text — never interpreted
    /// as markup, even when the source cell contains literal `&lt;br&gt;`-style text (R-20).</summary>
    public string? Preferences { get; set; }

    public string? ErrorMessage { get; set; }
}

/// <summary>
/// <see cref="Valid"/>/<see cref="Invalid"/> are the parse-time outcomes
/// (judge-import-file.md); <see cref="Excluded"/> is a resolution outcome set only via the
/// exclude action and — like <see cref="Valid"/> — never blocks consolidation (FR-056).
/// </summary>
public enum JudgeImportRowStatus
{
    Valid,
    Invalid,
    Excluded,
}
