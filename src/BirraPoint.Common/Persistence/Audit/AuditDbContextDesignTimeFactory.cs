using BirraPoint.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Common.Persistence.Audit;

internal sealed class AuditDbContextDesignTimeFactory : DesignTimeDbContextFactory<AuditDbContext>
{
    protected override AuditDbContext Create(DbContextOptions<AuditDbContext> options) => new(options);
}
