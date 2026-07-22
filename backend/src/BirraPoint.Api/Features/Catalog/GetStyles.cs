using BirraPoint.Api.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Catalog;

/// <summary>Lightweight BJCP 2021 catalog row for import matching / searchable pickers (contracts/rest-api.md §Catalog).</summary>
public sealed record StyleSummaryDto(string Code, string Name, string CategoryNumber, string CategoryName);

public sealed record GetStylesQuery : IRequest<IReadOnlyList<StyleSummaryDto>>;

public sealed class GetStylesQueryHandler(AppDbContext dbContext)
    : IRequestHandler<GetStylesQuery, IReadOnlyList<StyleSummaryDto>>
{
    public async Task<IReadOnlyList<StyleSummaryDto>> Handle(GetStylesQuery request, CancellationToken cancellationToken)
    {
        var styles = await dbContext.BjcpStyles
            .Select(style => new StyleSummaryDto(style.Code, style.Name, style.CategoryNumber, style.CategoryName))
            .ToListAsync(cancellationToken);

        // CategoryNumber is a varchar (Appendix B local styles use "X"), so a numeric-aware sort
        // has to happen client-side in .NET — a plain OrderBy would sort it lexicographically
        // ("1", "10".."19", "2", "20", ...) instead of by actual category number.
        return styles
            .OrderBy(style => int.TryParse(style.CategoryNumber, out var number) ? number : int.MaxValue)
            .ThenBy(style => style.CategoryNumber, StringComparer.Ordinal)
            .ThenBy(style => style.Code, StringComparer.Ordinal)
            .ToList();
    }
}

public static class CatalogEndpoints
{
    /// <summary>Maps <c>GET /api/v1/styles</c> (T017 — first slice; any authenticated user per the deny-by-default fallback policy).</summary>
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/styles", async (ISender sender, CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new GetStylesQuery(), cancellationToken)))
            .WithName("GetStyles")
            .WithTags("Catalog")
            .Produces<IReadOnlyList<StyleSummaryDto>>();

        // T059B: full style detail (vital statistics + guide description) for the judge-facing
        // evaluation-sheet reference panel (FR-049); any authenticated caller, same as GetStyles.
        endpoints.MapGet("/api/v1/styles/{code}", async (string code, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetStyleDetailQuery(code), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
            .WithName("GetStyleDetail")
            .WithTags("Catalog")
            .Produces<StyleDetailDto>()
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
