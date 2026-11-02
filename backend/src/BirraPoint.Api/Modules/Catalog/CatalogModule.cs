using BirraPoint.Api.Modules.Catalog.Contracts;
using BirraPoint.Api.Modules.Catalog.Integration;

namespace BirraPoint.Api.Modules.Catalog;

internal static class CatalogModule
{
    /// <summary>Registers the module's public contract implementations (scoped: they use the scoped DbContext).</summary>
    internal static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddScoped<IStyleCatalog, StyleCatalog>();
        return services;
    }
}
