namespace BirraPoint.Api.Common.Persistence;

/// <summary>
/// T095-T097: whether/how to apply EF Core migrations at API startup. Migrations always run in
/// Development (T009); outside Development they only run when Database:MigrateOnStartup=true —
/// Terraform sets it on the single long-running API replica, so every start of that replica
/// migrates (idempotently; EF's migration history table plus its session-scoped advisory lock
/// make repeat/concurrent runs safe). The connection string used for migrating is Neon's
/// non-pooled ConnectionStrings:dbDirect when configured (EF/Npgsql's migration advisory lock is
/// session-scoped and breaks under pgbouncer transaction pooling), falling back to
/// ConnectionStrings:db (e.g. local AppHost/Testcontainers Postgres, which isn't pooled at all).
/// </summary>
public static class StartupMigrations
{
    public static bool ShouldMigrate(bool isDevelopment, IConfiguration configuration) =>
        isDevelopment || configuration.GetValue("Database:MigrateOnStartup", false);

    public static string ResolveMigrationConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString("dbDirect")
        ?? configuration.GetConnectionString("db")
        ?? throw new InvalidOperationException("Neither ConnectionStrings:dbDirect nor ConnectionStrings:db is configured.");
}
