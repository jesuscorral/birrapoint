namespace BirraPoint.Api.Features.Catalog.Data;

/// <summary>Shape of one entry in the embedded bjcp-2021.json seed resource (T010, R-12).</summary>
public sealed class BjcpStyleSeedRecord
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string CategoryNumber { get; init; }

    public required string CategoryName { get; init; }

    public required VitalStatisticsSeed VitalStatistics { get; init; }

    public required StyleDescriptionSeed Description { get; init; }
}

public sealed class VitalStatisticsSeed
{
    public decimal? OgLow { get; init; }

    public decimal? OgHigh { get; init; }

    public decimal? FgLow { get; init; }

    public decimal? FgHigh { get; init; }

    public int? IbuLow { get; init; }

    public int? IbuHigh { get; init; }

    public decimal? SrmLow { get; init; }

    public decimal? SrmHigh { get; init; }

    public decimal? AbvLow { get; init; }

    public decimal? AbvHigh { get; init; }
}

public sealed class StyleDescriptionSeed
{
    public required string OverallImpression { get; init; }

    public required string Aroma { get; init; }

    public required string Appearance { get; init; }

    public required string Flavor { get; init; }

    public required string Mouthfeel { get; init; }

    public required string Comments { get; init; }

    public required string History { get; init; }

    public required string CharacteristicIngredients { get; init; }

    public required string StyleComparison { get; init; }

    public string? EntryInstructions { get; init; }

    public required IReadOnlyList<string> CommercialExamples { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }
}
