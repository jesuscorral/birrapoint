namespace BirraPoint.Common.Persistence;

/// <summary>PostgreSQL schema names: one per module, one per building block table (FR-068, FR-069). Each schema
/// belongs to exactly one data context, which also keeps the migration history of that schema (FR-097). Module
/// schemas are the lower-cased module name.</summary>
public static class DbSchemas
{
    public const string Catalog = "catalog";
    public const string Organization = "organization";
    public const string Judging = "judging";
    public const string Publication = "publication";
    public const string Jobs = "jobs";
    public const string Audit = "audit";

    /// <summary>Schema of the single shared migration history used before the per-context baselines (FR-098). Only the
    /// startup guard looks at it.</summary>
    public const string LegacyMigrations = "migrations";
}
