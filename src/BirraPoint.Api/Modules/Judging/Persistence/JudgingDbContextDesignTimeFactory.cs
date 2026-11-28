using BirraPoint.Api.Common.Persistence;
using BirraPoint.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Judging.Persistence;

internal sealed class JudgingDbContextDesignTimeFactory : DesignTimeDbContextFactory<JudgingDbContext>
{
    protected override JudgingDbContext Create(DbContextOptions<JudgingDbContext> options) => new(options);
}
