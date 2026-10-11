using BirraPoint.Api.Common.Persistence;
using Microsoft.Extensions.Configuration;

namespace BirraPoint.Api.UnitTests.Common.Persistence;

/// <summary>
/// T095-T097: pure decisions behind startup migrations — whether to run them, and which
/// connection string to migrate over — kept out of Program.cs so they're unit-testable.
/// </summary>
public sealed class StartupMigrationsTests
{
    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void DevelopmentAlwaysMigratesEvenWithoutTheConfigFlag()
    {
        var configuration = Build(new Dictionary<string, string?>());

        Assert.True(StartupMigrations.ShouldMigrate(isDevelopment: true, configuration));
    }

    [Fact]
    public void NonDevelopmentWithoutTheFlagDoesNotMigrate()
    {
        var configuration = Build(new Dictionary<string, string?>());

        Assert.False(StartupMigrations.ShouldMigrate(isDevelopment: false, configuration));
    }

    [Fact]
    public void NonDevelopmentWithMigrateOnStartupTrueMigrates()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Database:MigrateOnStartup"] = "true",
        });

        Assert.True(StartupMigrations.ShouldMigrate(isDevelopment: false, configuration));
    }

    [Fact]
    public void NonDevelopmentWithMigrateOnStartupFalseDoesNotMigrate()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Database:MigrateOnStartup"] = "false",
        });

        Assert.False(StartupMigrations.ShouldMigrate(isDevelopment: false, configuration));
    }

    [Fact]
    public void PrefersTheDirectConnectionStringWhenConfigured()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:db"] = "Host=pooled;Database=birrapoint",
            ["ConnectionStrings:dbDirect"] = "Host=direct;Database=birrapoint",
        });

        Assert.Equal("Host=direct;Database=birrapoint", StartupMigrations.ResolveMigrationConnectionString(configuration));
    }

    [Fact]
    public void FallsBackToThePooledConnectionStringWhenNoDirectStringIsConfigured()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:db"] = "Host=pooled;Database=birrapoint",
        });

        Assert.Equal("Host=pooled;Database=birrapoint", StartupMigrations.ResolveMigrationConnectionString(configuration));
    }

    [Fact]
    public void ThrowsWhenNeitherConnectionStringIsConfigured()
    {
        var configuration = Build(new Dictionary<string, string?>());

        Assert.Throws<InvalidOperationException>(() => StartupMigrations.ResolveMigrationConnectionString(configuration));
    }
}
