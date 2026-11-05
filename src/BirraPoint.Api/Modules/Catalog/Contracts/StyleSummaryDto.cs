namespace BirraPoint.Api.Modules.Catalog.Contracts;

/// <summary>Read-only projection of a BJCP 2021 style for other modules (FR-012, FR-049). Never the
/// <c>BjcpStyle</c> entity.</summary>
internal sealed record StyleSummaryDto(
    string Code,
    string Name,
    string CategoryNumber,
    string CategoryName,
    decimal? AbvLow,
    decimal? AbvHigh);
