using BirraPoint.Api.Modules.Organization.Features.Catalog.Features.GetStyleDetails;
using BirraPoint.Api.Modules.Organization.Features.Catalog.Features.GetStyles;

namespace BirraPoint.Api.Modules.Organization.Features.Catalog;

internal sealed record StyleSummaryDto(string Code, string Name, string CategoryNumber, string CategoryName);


internal static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/styles")
            .WithTags("Catalog");

        group.MapGetStylesEndpoint()
            .MapGetStyleDetailsEndpoint();

        return endpoints;
    }
}
