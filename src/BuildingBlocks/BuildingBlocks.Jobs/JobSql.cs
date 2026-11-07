using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BuildingBlocks.Jobs;

/// <summary>
/// Quoted table and column names of the host's <see cref="Job"/> mapping, read from the EF model so the
/// worker's raw SQL follows whatever names <see cref="JobsModelBuilderExtensions.ApplyJobs"/>'s
/// <c>configure</c> callback chose. Only identifiers are inlined; every value stays a SQL parameter.
/// </summary>
internal sealed class JobSql
{
    private JobSql(IEntityType entity)
    {
        var tableName = entity.GetTableName()
            ?? throw new InvalidOperationException($"{nameof(Job)} is not mapped to a table.");
        var schema = entity.GetSchema();
        var table = StoreObjectIdentifier.Table(tableName, schema);

        string Column(string property) => Quote(
            entity.FindProperty(property)!.GetColumnName(table)
            ?? throw new InvalidOperationException($"{nameof(Job)}.{property} is not mapped to a column."));

        Table = schema is null ? Quote(tableName) : $"{Quote(schema)}.{Quote(tableName)}";
        Status = Column(nameof(Job.Status));
        NextAttemptAt = Column(nameof(Job.NextAttemptAt));
        LeaseExpiresAt = Column(nameof(Job.LeaseExpiresAt));
        UpdatedAt = Column(nameof(Job.UpdatedAt));
        CreatedAt = Column(nameof(Job.CreatedAt));
    }

    public string Table { get; }

    public string Status { get; }

    public string NextAttemptAt { get; }

    public string LeaseExpiresAt { get; }

    public string UpdatedAt { get; }

    public string CreatedAt { get; }

    public static JobSql From(DbContext db) =>
        new(db.Model.FindEntityType(typeof(Job))
            ?? throw new InvalidOperationException($"{nameof(Job)} is not part of the model; call ApplyJobs."));

    /// <summary>Builds a parameterised <see cref="FormattableString"/>: <paramref name="format"/> holds the
    /// (already quoted) identifiers and positional <c>{0}</c> placeholders that EF turns into parameters.</summary>
    public static FormattableString Sql(string format, params object[] values) =>
        FormattableStringFactory.Create(format, values);

    /// <summary>Quotes an identifier for PostgreSQL and escapes braces so it survives composite formatting.</summary>
    private static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            .Replace("{", "{{", StringComparison.Ordinal)
            .Replace("}", "}}", StringComparison.Ordinal);
}
