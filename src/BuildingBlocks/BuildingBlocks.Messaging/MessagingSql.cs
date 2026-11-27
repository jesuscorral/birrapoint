using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BuildingBlocks.Messaging;

/// <summary>Raw-SQL helpers. Only identifiers (read from the EF model and quoted) are inlined; every value stays a
/// SQL parameter.</summary>
internal static class SqlText
{
    /// <summary>Builds a parameterised <see cref="FormattableString"/>: <paramref name="format"/> holds the
    /// (already quoted) identifiers and positional <c>{0}</c> placeholders that EF turns into parameters.</summary>
    public static FormattableString Sql(string format, params object[] values) =>
        FormattableStringFactory.Create(format, values);

    /// <summary>Quotes an identifier for PostgreSQL and escapes braces so it survives composite formatting.</summary>
    public static string Quote(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            .Replace("{", "{{", StringComparison.Ordinal)
            .Replace("}", "}}", StringComparison.Ordinal);
}

/// <summary>Quoted table and column names of one module's outbox, read from the EF model.</summary>
internal sealed class OutboxSql
{
    private OutboxSql(IEntityType entity)
    {
        var tableName = entity.GetTableName()
            ?? throw new InvalidOperationException($"{entity.Name} is not mapped to a table.");
        var schema = entity.GetSchema();
        var table = StoreObjectIdentifier.Table(tableName, schema);

        string Column(string property) => SqlText.Quote(
            entity.FindProperty(property)!.GetColumnName(table)
            ?? throw new InvalidOperationException($"{entity.Name}.{property} is not mapped to a column."));

        Display = schema is null ? tableName : $"{schema}.{tableName}";
        Table = schema is null ? SqlText.Quote(tableName) : $"{SqlText.Quote(schema)}.{SqlText.Quote(tableName)}";
        Id = Column(nameof(OutboxMessage.Id));
        Status = Column(nameof(OutboxMessage.Status));
        Attempts = Column(nameof(OutboxMessage.Attempts));
        OccurredAt = Column(nameof(OutboxMessage.OccurredAt));
        NextAttemptAt = Column(nameof(OutboxMessage.NextAttemptAt));
        LeaseOwner = Column(nameof(OutboxMessage.LeaseOwner));
        LeaseExpiresAt = Column(nameof(OutboxMessage.LeaseExpiresAt));
        LastError = Column(nameof(OutboxMessage.LastError));
    }

    /// <summary>Unquoted <c>schema.table</c>, for logs.</summary>
    public string Display { get; }

    public string Table { get; }

    public string Id { get; }

    public string Status { get; }

    public string Attempts { get; }

    public string OccurredAt { get; }

    public string NextAttemptAt { get; }

    public string LeaseOwner { get; }

    public string LeaseExpiresAt { get; }

    public string LastError { get; }

    public static OutboxSql From(DbContext db, string outbox) =>
        new(db.Model.FindEntityType(MessagingNames.Outbox(outbox))
            ?? throw new InvalidOperationException($"Outbox '{outbox}' is not part of the model; call ApplyOutbox(\"{outbox}\", schema)."));
}

/// <summary>Quoted table and column names of one module's inbox, read from the EF model.</summary>
internal sealed class InboxSql
{
    private InboxSql(IEntityType entity)
    {
        var tableName = entity.GetTableName()
            ?? throw new InvalidOperationException($"{entity.Name} is not mapped to a table.");
        var schema = entity.GetSchema();
        var table = StoreObjectIdentifier.Table(tableName, schema);

        string Column(string property) => SqlText.Quote(
            entity.FindProperty(property)!.GetColumnName(table)
            ?? throw new InvalidOperationException($"{entity.Name}.{property} is not mapped to a column."));

        Display = schema is null ? tableName : $"{schema}.{tableName}";
        Table = schema is null ? SqlText.Quote(tableName) : $"{SqlText.Quote(schema)}.{SqlText.Quote(tableName)}";
        EventId = Column(nameof(InboxMessage.EventId));
        Handler = Column(nameof(InboxMessage.Handler));
        ProcessedAt = Column(nameof(InboxMessage.ProcessedAt));
    }

    /// <summary>Unquoted <c>schema.table</c>, for logs.</summary>
    public string Display { get; }

    public string Table { get; }

    public string EventId { get; }

    public string Handler { get; }

    public string ProcessedAt { get; }

    public static InboxSql From(DbContext db, string inbox) =>
        new(db.Model.FindEntityType(MessagingNames.Inbox(inbox))
            ?? throw new InvalidOperationException($"Inbox '{inbox}' is not part of the model; call ApplyInbox(\"{inbox}\", schema)."));
}
