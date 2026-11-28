using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Catalog.Integration;
using BirraPoint.Api.Modules.Catalog.Persistence;
using BirraPoint.Contracts.Catalog;

namespace BirraPoint.Api.Modules.Catalog;

internal static class CatalogModule
{
    /// <summary>Registers the module's public contract implementations (scoped: they use the module's scoped context).</summary>
    internal static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        services.AddBirraPointDbContext<CatalogDbContext>();
        services.AddScoped<IStyleCatalog, StyleCatalog>();
        return services;
    }
}
