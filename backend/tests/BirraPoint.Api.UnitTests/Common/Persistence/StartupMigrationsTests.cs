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
    public void Development_always_migrates_even_without_the_config_flag()
    {
        var configuration = Build(new Dictionary<string, string?>());

        Assert.True(StartupMigrations.ShouldMigrate(isDevelopment: true, configuration));
    }

    [Fact]
    public void Non_development_without_the_flag_does_not_migrate()
    {
        var configuration = Build(new Dictionary<string, string?>());

        Assert.False(StartupMigrations.ShouldMigrate(isDevelopment: false, configuration));
    }

    [Fact]
    public void Non_development_with_MigrateOnStartup_true_migrates()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Database:MigrateOnStartup"] = "true",
        });

        Assert.True(StartupMigrations.ShouldMigrate(isDevelopment: false, configuration));
    }

    [Fact]
    public void Non_development_with_MigrateOnStartup_false_does_not_migrate()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Database:MigrateOnStartup"] = "false",
        });

        Assert.False(StartupMigrations.ShouldMigrate(isDevelopment: false, configuration));
    }

    [Fact]
    public void Prefers_the_direct_connection_string_when_configured()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:db"] = "Host=pooled;Database=birrapoint",
            ["ConnectionStrings:dbDirect"] = "Host=direct;Database=birrapoint",
        });

        Assert.Equal("Host=direct;Database=birrapoint", StartupMigrations.ResolveMigrationConnectionString(configuration));
    }

    [Fact]
    public void Falls_back_to_the_pooled_connection_string_when_no_direct_string_is_configured()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:db"] = "Host=pooled;Database=birrapoint",
        });

        Assert.Equal("Host=pooled;Database=birrapoint", StartupMigrations.ResolveMigrationConnectionString(configuration));
    }

    [Fact]
    public void Throws_when_neither_connection_string_is_configured()
    {
        var configuration = Build(new Dictionary<string, string?>());

        Assert.Throws<InvalidOperationException>(() => StartupMigrations.ResolveMigrationConnectionString(configuration));
    }
}
