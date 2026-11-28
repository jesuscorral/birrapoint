using BirraPoint.Api.Common.Persistence;
using BirraPoint.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Organization.Persistence;

internal sealed class OrganizationDbContextDesignTimeFactory : DesignTimeDbContextFactory<OrganizationDbContext>
{
    protected override OrganizationDbContext Create(DbContextOptions<OrganizationDbContext> options) => new(options);
}
