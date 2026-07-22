using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Catalog;

/// <summary>Nested `vitalStatistics` shape (contracts/rest-api.md §Catalog, FR-049).</summary>
public sealed record StyleVitalStatisticsDto(
    decimal? OgLow, decimal? OgHigh,
    decimal? FgLow, decimal? FgHigh,
    int? IbuLow, int? IbuHigh,
    decimal? SrmLow, decimal? SrmHigh,
    decimal? AbvLow, decimal? AbvHigh);

/// <summary>Nested `description` shape — the BJCP 2021 guide text (data-model.md §BjcpStyle.DescriptionJson,
/// FR-049). Property names/casing match the jsonb column verbatim (see BjcpStyleCatalogLoader/the
/// AddBjcpStyleCatalogDetails migration, which serializes with camelCase), so deserializing
/// straight into this DTO round-trips without any manual remapping.</summary>
public sealed record StyleDescriptionDto(
    string OverallImpression,
    string Aroma,
    string Appearance,
    string Flavor,
    string Mouthfeel,
    string Comments,
    string History,
    string CharacteristicIngredients,
    string StyleComparison,
    string? EntryInstructions,
    IReadOnlyList<string> CommercialExamples,
    IReadOnlyList<string> Tags);

/// <summary>Full BJCP 2021 style detail for the judge-facing evaluation-sheet reference panel
/// (contracts/rest-api.md §Catalog, FR-049). No entrant/anonymity concern — the BJCP catalog is
/// public reference data, not competition-specific.</summary>
public sealed record StyleDetailDto(
    string Code,
    string Name,
    string CategoryNumber,
    string CategoryName,
    StyleVitalStatisticsDto VitalStatistics,
    StyleDescriptionDto Description);

/// <summary>GET /styles/{code} (contracts/rest-api.md §Catalog, T059B, FR-049). Returns null when
/// no BjcpStyle matches — the endpoint maps that to a plain 404.</summary>
public sealed record GetStyleDetailQuery(string Code) : IRequest<StyleDetailDto?>;

public sealed class GetStyleDetailQueryHandler(AppDbContext dbContext) : IRequestHandler<GetStyleDetailQuery, StyleDetailDto?>
{
    // Mirrors BjcpStyleCatalogLoader's PropertyNameCaseInsensitive convention for parsing the
    // catalog's JSON shape.
    private static readonly JsonSerializerOptions DescriptionJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<StyleDetailDto?> Handle(GetStyleDetailQuery request, CancellationToken cancellationToken)
    {
        // Case-insensitive code match, same convention WorkbookParser uses for import style
        // matching (FR-010) — Code is a natural-key PK (e.g. "21A"), never expected to collide
        // case-insensitively within the catalog.
        var style = await dbContext.BjcpStyles
            .FirstOrDefaultAsync(s => s.Code.ToUpper() == request.Code.ToUpper(), cancellationToken);

        if (style is null)
        {
            return null;
        }

        var description = JsonSerializer.Deserialize<StyleDescriptionDto>(style.DescriptionJson, DescriptionJsonOptions)
            ?? throw new InvalidOperationException($"BjcpStyle '{style.Code}' has an unparsable DescriptionJson.");

        var vitalStatistics = new StyleVitalStatisticsDto(
            style.OGLow, style.OGHigh,
            style.FGLow, style.FGHigh,
            style.IBULow, style.IBUHigh,
            style.SRMLow, style.SRMHigh,
            style.ABVLow, style.ABVHigh);

        return new StyleDetailDto(style.Code, style.Name, style.CategoryNumber, style.CategoryName, vitalStatistics, description);
    }
}
