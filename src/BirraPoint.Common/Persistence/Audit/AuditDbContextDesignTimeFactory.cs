using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Common.Persistence.Audit;

public sealed class AuditDbContextDesignTimeFactory : DesignTimeDbContextFactory<AuditDbContext>
{
    protected override AuditDbContext Create(DbContextOptions<AuditDbContext> options) => new(options);
}
