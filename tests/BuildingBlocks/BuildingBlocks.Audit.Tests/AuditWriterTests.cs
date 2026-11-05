using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Audit.Tests;

/// <summary>AuditWriter stages an AuditLog row on the host's DbContext without calling SaveChanges, so the
/// audit entry commits atomically with whatever business change the caller persists.</summary>
public sealed class AuditWriterTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 18, 30, 0, TimeSpan.Zero);

    private TestDbContext NewContext() => new(fixture.Options);

    private static AuditWriter<TestDbContext> Writer(TestDbContext db, string actor = "actor-42") =>
        new(db, new FixedActor(actor), new FixedTimeProvider(Now));

    [Fact]
    public async Task RecordStagesARowThatPersistsOnTheCallersSaveChanges()
    {
        await using var db = NewContext();
        var entityId = Guid.NewGuid().ToString();

        Writer(db).Record("OrderCorrected", "Order", entityId,
            before: new Dictionary<string, object?>(StringComparer.Ordinal) { ["Total"] = 30 },
            after: new Dictionary<string, object?>(StringComparer.Ordinal) { ["Total"] = 35 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var verify = NewContext();
        var stored = await verify.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.EntityId == entityId, TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, stored.Id);
        Assert.Equal("actor-42", stored.ActorUserId);
        Assert.Equal("OrderCorrected", stored.Action);
        Assert.Equal("Order", stored.EntityType);
        Assert.Equal(Now, stored.OccurredAt);

        using var data = JsonDocument.Parse(stored.DataJson);
        Assert.Equal(30, data.RootElement.GetProperty("before").GetProperty("Total").GetInt32());
        Assert.Equal(35, data.RootElement.GetProperty("after").GetProperty("Total").GetInt32());
    }

    [Fact]
    public async Task RecordAcceptsANullBeforeForCreationStyleActions()
    {
        await using var db = NewContext();
        var entityId = Guid.NewGuid().ToString();

        Writer(db).Record("UserInvited", "User", entityId, before: null, after: new { Email = "user@example.test" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var verify = NewContext();
        var stored = await verify.AuditLogs.AsNoTracking()
            .SingleAsync(a => a.EntityId == entityId, TestContext.Current.CancellationToken);

        using var data = JsonDocument.Parse(stored.DataJson);
        Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("before").ValueKind);
        Assert.Equal("user@example.test", data.RootElement.GetProperty("after").GetProperty("Email").GetString());
    }

    [Fact]
    public async Task NothingPersistsWhenTheCallerNeverCallsSaveChanges()
    {
        var entityId = Guid.NewGuid().ToString();
        await using (var db = NewContext())
        {
            Writer(db).Record("Closed", "Table", entityId);
            Assert.True(db.ChangeTracker.HasChanges());
        }

        await using var verify = NewContext();
        Assert.False(await verify.AuditLogs.AsNoTracking()
            .AnyAsync(a => a.EntityId == entityId, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("", "Entity", "1")]
    [InlineData("Action", " ", "1")]
    [InlineData("Action", "Entity", "")]
    public async Task RecordRejectsBlankIdentifiers(string action, string entityType, string entityId)
    {
        await using var db = NewContext();

        Assert.ThrowsAny<ArgumentException>(() => Writer(db).Record(action, entityType, entityId));
    }
}
