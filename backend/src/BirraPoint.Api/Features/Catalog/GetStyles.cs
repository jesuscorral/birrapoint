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
        return await dbContext.BjcpStyles
            .OrderBy(style => style.CategoryNumber)
            .ThenBy(style => style.Code)
            .Select(style => new StyleSummaryDto(style.Code, style.Name, style.CategoryNumber, style.CategoryName))
            .ToListAsync(cancellationToken);
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

        return endpoints;
    }
}
