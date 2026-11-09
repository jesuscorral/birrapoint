using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BuildingBlocks.Messaging.Tests;

/// <summary>ApplyOutbox / ApplyInbox map the shared-type entities with the columns, keys and indexes of the
/// living data model (R-106-2).</summary>
public sealed class MessagingModelTests
{
    private static TestDb NewDb() =>
        new(new DbContextOptionsBuilder<TestDb>().UseNpgsql("Host=localhost").Options);

    [Fact]
    public void OutboxMapsToItsModuleSchemaWithTheDocumentedColumns()
    {
        using var db = NewDb();

        var entity = db.Model.FindEntityType(MessagingNames.Outbox("Orders"))!;

        Assert.Equal("OutboxMessages", entity.GetTableName());
        Assert.Equal("orders", entity.GetSchema());
        Assert.Equal([nameof(OutboxMessage.Id)], entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(200, entity.FindProperty(nameof(OutboxMessage.Type))!.GetMaxLength());
        Assert.Equal("jsonb", entity.FindProperty(nameof(OutboxMessage.PayloadJson))!.GetColumnType());
        Assert.Equal(20, entity.FindProperty(nameof(OutboxMessage.Status))!.GetMaxLength());
        Assert.Equal(typeof(string), entity.FindProperty(nameof(OutboxMessage.Status))!.GetProviderClrType());
        Assert.Equal(200, entity.FindProperty(nameof(OutboxMessage.LeaseOwner))!.GetMaxLength());
        Assert.Equal(2000, entity.FindProperty(nameof(OutboxMessage.LastError))!.GetMaxLength());
        Assert.Equal(ValueGenerated.Never, entity.FindProperty(nameof(OutboxMessage.Id))!.ValueGenerated);
        Assert.Equal(
            ["Status,LeaseExpiresAt", "Status,NextAttemptAt"],
            entity.GetIndexes().Select(i => string.Join(',', i.Properties.Select(p => p.Name))).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void InboxMapsToItsModuleSchemaWithACompositeKey()
    {
        using var db = NewDb();

        var entity = db.Model.FindEntityType(MessagingNames.Inbox("Shipping"))!;

        Assert.Equal("InboxMessages", entity.GetTableName());
        Assert.Equal("shipping", entity.GetSchema());
        Assert.Equal(
            [nameof(InboxMessage.EventId), nameof(InboxMessage.Handler)],
            entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Equal(200, entity.FindProperty(nameof(InboxMessage.Handler))!.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Single().Name == nameof(InboxMessage.ProcessedAt));
    }

    [Fact]
    public void TwoOutboxesAndAnInboxWithTheSameNameCoexistOnOneModel()
    {
        using var db = NewDb();

        var tables = db.Model.GetEntityTypes()
            .Where(e => e.ClrType == typeof(OutboxMessage) || e.ClrType == typeof(InboxMessage))
            .Select(e => $"{e.GetSchema()}.{e.GetTableName()}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["billing.OutboxMessages", "orders.InboxMessages", "orders.OutboxMessages", "shipping.InboxMessages"],
            tables);
    }
}
