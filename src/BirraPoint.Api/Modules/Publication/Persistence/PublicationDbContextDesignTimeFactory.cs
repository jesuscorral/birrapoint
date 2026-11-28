using BirraPoint.Api.Common.Persistence;
using BirraPoint.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Publication.Persistence;

internal sealed class PublicationDbContextDesignTimeFactory : DesignTimeDbContextFactory<PublicationDbContext>
{
    protected override PublicationDbContext Create(DbContextOptions<PublicationDbContext> options) => new(options);
}
