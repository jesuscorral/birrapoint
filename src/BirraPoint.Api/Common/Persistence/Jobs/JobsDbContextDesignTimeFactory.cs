using BirraPoint.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Common.Persistence.Jobs;

internal sealed class JobsDbContextDesignTimeFactory : DesignTimeDbContextFactory<JobsDbContext>
{
    protected override JobsDbContext Create(DbContextOptions<JobsDbContext> options) => new(options);
}
