using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Common.Persistence;

/// <summary>
/// Whether/how to apply EF Core migrations at API startup. Migrations always run in
/// Development; outside Development they only run when Database:MigrateOnStartup=true —
/// Terraform sets it on the single long-running API replica, so every start of that replica
/// migrates (idempotently; each context's own history table plus EF's migration lock make
/// repeat/concurrent runs safe). The connection string used for migrating is Neon's
/// non-pooled ConnectionStrings:dbDirect when configured (EF/Npgsql's migration advisory lock is
/// session-scoped and breaks under pgbouncer transaction pooling), falling back to
/// ConnectionStrings:db (e.g. local AppHost/Testcontainers Postgres, which isn't pooled at all).
/// <para>
/// Six contexts migrate one after another in <see cref="ContextsInOrder"/> (FR-097): the building-block stores first,
/// then the modules in dependency order. A failure stops startup and logs which context failed; the contexts before it
/// stay migrated.
/// </para>
/// </summary>
internal static partial class StartupMigrations
{
    /// <summary>Context names (type name without the DbContext suffix) in the order they migrate (FR-097). Every registered context must be listed here.</summary>
    public static readonly IReadOnlyList<string> ContextsInOrder =
    [
        "Jobs",
        "Audit",
        "Catalog",
        "Organization",
        "Judging",
        "Publication",
    ];

    public static bool ShouldMigrate(bool isDevelopment, IConfiguration configuration) =>
        isDevelopment || configuration.GetValue("Database:MigrateOnStartup", false);

    public static string ResolveMigrationConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString("dbDirect")
        ?? configuration.GetConnectionString("db")
        ?? throw new InvalidOperationException("Neither ConnectionStrings:dbDirect nor ConnectionStrings:db is configured.");

    /// <summary>Refuses a legacy database once, then migrates every registered context in order over the migration
    /// connection string.</summary>
    public static async Task MigrateAllAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var targets = services.GetServices<MigrationTarget>().ToDictionary(t => t.Name, StringComparer.Ordinal);
        var missing = ContextsInOrder.Where(name => !targets.ContainsKey(name)).ToList();
        var unlisted = targets.Keys.Except(ContextsInOrder, StringComparer.Ordinal).ToList();
        if (missing.Count > 0 || unlisted.Count > 0)
        {
            throw new InvalidOperationException(
                $"Registered contexts and {nameof(ContextsInOrder)} differ (not registered: [{string.Join(", ", missing)}]; not listed: [{string.Join(", ", unlisted)}]).");
        }

        var connectionString = ResolveMigrationConnectionString(configuration);
        var contexts = ContextsInOrder.Select(name => (Name: name, Context: targets[name].Create(connectionString))).ToList();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(StartupMigrations).FullName!);
        try
        {
            await LegacyDatabaseGuard.EnsureCompatibleAsync([.. contexts.Select(c => c.Context)], cancellationToken);

            foreach (var (name, context) in contexts)
            {
                try
                {
                    await context.Database.MigrateAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    MigrationFailed(logger, ex, name);
                    throw;
                }
            }
        }
        finally
        {
            foreach (var (_, context) in contexts)
            {
                await context.DisposeAsync();
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Startup migration failed in {Context}; the contexts before it are migrated, startup stops.")]
    private static partial void MigrationFailed(ILogger logger, Exception exception, string context);
}
