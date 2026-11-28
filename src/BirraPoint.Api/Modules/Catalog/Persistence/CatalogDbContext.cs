using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Modules.Catalog.Domain;
using BirraPoint.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Catalog.Persistence;

/// <summary>The <c>catalog</c> schema: the read-only BJCP 2021 style catalog (FR-049). Other modules reach it only
/// through <c>IStyleCatalog</c>, never with a foreign key (FR-093).</summary>
internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, SharedTransaction? sharedTransaction = null)
    : BirraPointDbContext(options, DbSchemas.Catalog, sharedTransaction)
{
    public DbSet<BjcpStyle> BjcpStyles => Set<BjcpStyle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplyConfigurationsOf(modelBuilder, "BirraPoint.Api.Modules.Catalog.Persistence");
    }
}
