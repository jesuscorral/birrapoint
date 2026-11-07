using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BuildingBlocks.Jobs.Tests;

/// <summary>ApplyJobs maps the queue entity with the defaults the worker relies on; the host refines the rest.</summary>
public sealed class JobsModelTests
{
    private sealed class DefaultDb(DbContextOptions<DefaultDb> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyJobs();
    }

    private static IEntityType Entity(DbContext db) => db.Model.FindEntityType(typeof(Job))!;

    private static DefaultDb NewDefaultDb() =>
        new(new DbContextOptionsBuilder<DefaultDb>().UseNpgsql("Host=localhost").Options);

    private static TestDb NewTestDb() =>
        new(new DbContextOptionsBuilder<TestDb>().UseNpgsql("Host=localhost").Options);

    [Fact]
    public void DefaultsStoreEnumsAsStringsAndPayloadAsJsonb()
    {
        using var db = NewDefaultDb();
        var entity = Entity(db);

        Assert.Equal("Jobs", entity.GetTableName());
        Assert.Equal(20, entity.FindProperty(nameof(Job.Status))!.GetMaxLength());
        Assert.Equal(typeof(string), entity.FindProperty(nameof(Job.Status))!.GetProviderClrType());
        Assert.Equal("jsonb", entity.FindProperty(nameof(Job.PayloadJson))!.GetColumnType());
        Assert.Equal(2000, entity.FindProperty(nameof(Job.LastError))!.GetMaxLength());
        Assert.Equal(200, entity.FindProperty(nameof(Job.LeaseOwner))!.GetMaxLength());
        Assert.NotNull(entity.FindProperty(nameof(Job.Type))!.GetMaxLength());
        Assert.False(entity.FindProperty(nameof(Job.GroupKey))!.IsNullable);
    }

    [Fact]
    public void DeclaresTheTwoSweepIndexes()
    {
        using var db = NewDefaultDb();

        var indexes = Entity(db).GetIndexes()
            .Select(i => string.Join(",", i.Properties.Select(p => p.Name)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Status,LeaseExpiresAt", "Status,NextAttemptAt"], indexes);
    }

    [Fact]
    public void HostConfigurationOverridesTableAndColumns()
    {
        using var db = NewTestDb();
        var entity = Entity(db);
        var table = StoreObjectIdentifier.Table(TestDb.TableName, TestDb.SchemaName);

        Assert.Equal(TestDb.TableName, entity.GetTableName());
        Assert.Equal(TestDb.SchemaName, entity.GetSchema());
        Assert.Equal("grp", entity.FindProperty(nameof(Job.GroupKey))!.GetColumnName(table));
        Assert.Equal(40, entity.FindProperty(nameof(Job.Type))!.GetMaxLength());
    }
}
