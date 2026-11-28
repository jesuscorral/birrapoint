using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace BirraPoint.Common.Persistence;

/// <summary>A context the startup migration runs, created over the migration connection string.</summary>
internal sealed record MigrationTarget(string Name, Func<string, BirraPointDbContext> Create);

internal static class DbContextRegistration
{
    /// <summary>
    /// Registers what every context shares: one <see cref="NpgsqlDataSource"/> over <c>ConnectionStrings:db</c>
    /// (read when first needed, so test hosts can override it), one scoped <see cref="NpgsqlConnection"/> per DI scope
    /// used by all contexts (FR-096), and the unit-of-work interceptors. Idempotent.
    /// </summary>
    public static IServiceCollection AddBirraPointPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(sp =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("db")
                ?? throw new InvalidOperationException("ConnectionStrings:db is not configured.");
            return NpgsqlDataSource.Create(connectionString);
        });
        services.TryAddScoped(sp => sp.GetRequiredService<NpgsqlDataSource>().CreateConnection());
        services.TryAddScoped<SharedTransaction>();
        services.TryAddScoped<SharedTransactionInterceptor>();
        services.TryAddScoped<UnitOfWorkSaveChangesInterceptor>();
        return services;
    }

    /// <summary>
    /// Registers <typeparamref name="TContext"/> on the scope's shared connection with the unit-of-work and messaging
    /// interceptors, and as a startup migration target (ADR-0043).
    /// </summary>
    public static IServiceCollection AddBirraPointDbContext<TContext>(this IServiceCollection services)
        where TContext : BirraPointDbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddBirraPointPersistence();
        if (services.Any(d => d.ServiceType == typeof(TContext)))
        {
            return services;
        }

        services.AddDbContext<TContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<NpgsqlConnection>())
            .AddInterceptors(
                sp.GetRequiredService<SharedTransactionInterceptor>(),
                sp.GetRequiredService<UnitOfWorkSaveChangesInterceptor>())
            .AddMessagingInterceptors(sp));
        services.AddSingleton(new MigrationTarget(typeof(TContext).Name.Replace("DbContext", string.Empty, StringComparison.Ordinal), CreateForMigration<TContext>));
        return services;
    }

    private static TContext CreateForMigration<TContext>(string connectionString)
        where TContext : BirraPointDbContext
    {
        var options = new DbContextOptionsBuilder<TContext>().UseNpgsql(connectionString).Options;
        return (TContext)Activator.CreateInstance(typeof(TContext), options, null)!;
    }
}
