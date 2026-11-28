using BirraPoint.Api.Common.Persistence;
using BirraPoint.Common.Persistence;
using BirraPoint.Common.Persistence.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.UnitTests.Common.Persistence;

/// <summary>Registering a data context is idempotent: the startup migration keys its targets by context name (FR-096).</summary>
public sealed class DbContextRegistrationTests
{
    [Fact]
    public void RegisteringTheSameContextTwiceAddsOneMigrationTarget()
    {
        var services = new ServiceCollection();

        services.AddBirraPointDbContext<JobsDbContext>();
        services.AddBirraPointDbContext<JobsDbContext>();

        Assert.Single(services, d => d.ServiceType == typeof(MigrationTarget));
        Assert.Single(services, d => d.ServiceType == typeof(JobsDbContext));
    }
}
