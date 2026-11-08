namespace BirraPoint.Api.Common.Persistence;

/// <summary>PostgreSQL schema names: one per module, one per building block table, one for the migration
/// history (FR-068, FR-069, FR-073). Module schemas are the lower-cased module name.</summary>
internal static class DbSchemas
{
    public const string Catalog = "catalog";
    public const string Organization = "organization";
    public const string Judging = "judging";
    public const string Publication = "publication";
    public const string Jobs = "jobs";
    public const string Audit = "audit";
    public const string Migrations = "migrations";

    /// <summary>Maps the module segment of <c>BirraPoint.Api.Modules.&lt;Module&gt;.*</c> to its schema.</summary>
    public static readonly IReadOnlyDictionary<string, string> ByModule = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Catalog"] = Catalog,
        ["Organization"] = Organization,
        ["Judging"] = Judging,
        ["Publication"] = Publication,
    };
}
