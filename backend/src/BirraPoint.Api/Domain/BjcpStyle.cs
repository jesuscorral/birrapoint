namespace BirraPoint.Api.Domain;

/// <summary>Read-only BJCP 2021 catalog row, seeded via EF migration (FR-012, R-12).</summary>
public class BjcpStyle : ITimestamped
{
    /// <summary>Natural PK, e.g. "21A".</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public required string CategoryNumber { get; set; }

    public required string CategoryName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
