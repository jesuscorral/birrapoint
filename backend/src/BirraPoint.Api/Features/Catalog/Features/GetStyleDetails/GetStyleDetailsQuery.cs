using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Catalog.Features.GetStyleDetails;

/// <summary>Nested `vitalStatistics` shape (contracts/rest-api.md §Catalog, FR-049).</summary>
internal sealed record StyleVitalStatisticsDto(
    decimal? OgLow, decimal? OgHigh,
    decimal? FgLow, decimal? FgHigh,
    int? IbuLow, int? IbuHigh,
    decimal? SrmLow, decimal? SrmHigh,
    decimal? AbvLow, decimal? AbvHigh);

internal sealed record StyleDescriptionDto(
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
internal sealed record StyleDetailDto(
    string Code,
    string Name,
    string CategoryNumber,
    string CategoryName,
    StyleVitalStatisticsDto VitalStatistics,
    StyleDescriptionDto Description);

/// <summary>GET /styles/{code} (contracts/rest-api.md §Catalog, FR-049). Returns null when
/// no BjcpStyle matches — the endpoint maps that to a plain 404.</summary>
internal sealed record GetStyleDetailQuery(string Code) : IRequest<StyleDetailDto?>;

internal sealed class GetStyleDetailQueryHandler(AppDbContext dbContext) : IRequestHandler<GetStyleDetailQuery, StyleDetailDto?>
{
    // Mirrors BjcpStyleCatalogLoader's PropertyNameCaseInsensitive convention for parsing the
    // catalog's JSON shape.
    private static readonly JsonSerializerOptions DescriptionJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<StyleDetailDto?> Handle(GetStyleDetailQuery request, CancellationToken cancellationToken)
    {
        // Case-insensitive code match (e.g. "21a" finds "21A"), evaluated in PostgreSQL.
        var codePattern = LikePattern.Escape(request.Code);
        var style = await dbContext.BjcpStyles
            .FirstOrDefaultAsync(s => EF.Functions.ILike(s.Code, codePattern, LikePattern.EscapeCharacter), cancellationToken);

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
