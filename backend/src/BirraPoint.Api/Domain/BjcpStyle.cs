namespace BirraPoint.Api.Domain;

/// <summary>Read-only BJCP 2021 catalog row, seeded via EF migration (FR-012, FR-049, R-12).</summary>
public class BjcpStyle : ITimestamped
{
    /// <summary>Natural PK, e.g. "21A", or a slug for styles with no official letter subcode, e.g. "27-Sahti".</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public required string CategoryNumber { get; set; }

    public required string CategoryName { get; set; }

    public decimal? OGLow { get; set; }

    public decimal? OGHigh { get; set; }

    public decimal? FGLow { get; set; }

    public decimal? FGHigh { get; set; }

    public int? IBULow { get; set; }

    public int? IBUHigh { get; set; }

    public decimal? SRMLow { get; set; }

    public decimal? SRMHigh { get; set; }

    public decimal? ABVLow { get; set; }

    public decimal? ABVHigh { get; set; }

    /// <summary>Full BJCP 2021 guide description (jsonb) — see data-model.md §BjcpStyle.DescriptionJson.</summary>
    public required string DescriptionJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
