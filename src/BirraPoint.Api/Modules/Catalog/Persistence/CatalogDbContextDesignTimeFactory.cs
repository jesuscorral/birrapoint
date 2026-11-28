using BirraPoint.Api.Common.Persistence;
using BirraPoint.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Catalog.Persistence;

internal sealed class CatalogDbContextDesignTimeFactory : DesignTimeDbContextFactory<CatalogDbContext>
{
    protected override CatalogDbContext Create(DbContextOptions<CatalogDbContext> options) => new(options);
}
