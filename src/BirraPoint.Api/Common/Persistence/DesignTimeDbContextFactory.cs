using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BirraPoint.Api.Common.Persistence;

/// <summary>
/// Used only by `dotnet ef migrations …` at design time; no connection is opened when
/// generating migrations, so the connection string is loaded from environment variables.
/// One thin subclass per context supplies its construction (ADR-0043).
/// </summary>
internal abstract class DesignTimeDbContextFactory<TContext> : IDesignTimeDbContextFactory<TContext>
    where TContext : BirraPointDbContext
{
    public TContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("BIRRAPOINT_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var host = Environment.GetEnvironmentVariable("BIRRAPOINT_DB_HOST") ?? "localhost";
            var database = Environment.GetEnvironmentVariable("BIRRAPOINT_DB_NAME") ?? "birrapoint-design";
            var username = Environment.GetEnvironmentVariable("BIRRAPOINT_DB_USERNAME") ?? "postgres";
            var password = Environment.GetEnvironmentVariable("BIRRAPOINT_DB_PASSWORD") ?? string.Empty;

            connectionString = $"Host={host};Database={database};Username={username};Password={password}";
        }

        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(connectionString)
            .Options;

        return Create(options);
    }

    protected abstract TContext Create(DbContextOptions<TContext> options);
}
