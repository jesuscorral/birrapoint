using BirraPoint.Api.Modules.Catalog.Features.Catalog;

namespace BirraPoint.Api.Modules.Catalog;

internal static class CatalogModuleEndpoints
{
    public static IEndpointRouteBuilder MapCatalogModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapCatalogEndpoints();

        return endpoints;
    }
}
