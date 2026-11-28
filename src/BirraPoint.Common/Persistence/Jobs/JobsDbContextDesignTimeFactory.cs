using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Common.Persistence.Jobs;

public sealed class JobsDbContextDesignTimeFactory : DesignTimeDbContextFactory<JobsDbContext>
{
    protected override JobsDbContext Create(DbContextOptions<JobsDbContext> options) => new(options);
}
