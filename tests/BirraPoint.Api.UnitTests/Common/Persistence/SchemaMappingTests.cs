using BirraPoint.Api.Common.Persistence;
using BuildingBlocks.Audit;
using BuildingBlocks.Jobs;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BirraPoint.Api.UnitTests.Common.Persistence;

/// <summary>Every table sits in its owner's schema and the migration history is kept out of public
/// (FR-068, FR-073, SC-015).</summary>
public sealed class SchemaMappingTests
{
    private static readonly (string Module, string Schema)[] Modules =
    [("Catalog", "catalog"), ("Organization", "organization"), ("Judging", "judging"), ("Publication", "publication")];

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost").Options);

    private static string ExpectedSchema(IEntityType entity)
    {
        if (entity.ClrType == typeof(Job))
        {
            return "jobs";
        }

        if (entity.ClrType == typeof(AuditLog))
        {
            return "audit";
        }

        // Outbox/inbox of the messaging block are shared-type entities named "<Outbox|Inbox>:<Module>"; each lives
        // in the schema of the module that publishes or subscribes.
        if (entity.ClrType == typeof(OutboxMessage) || entity.ClrType == typeof(InboxMessage))
        {
            var owner = entity.Name[(entity.Name.IndexOf(':', StringComparison.Ordinal) + 1)..];
            return Modules.Single(m => m.Module == owner).Schema;
        }

        var ns = entity.ClrType.Namespace ?? string.Empty;
        foreach (var (module, schema) in Modules)
        {
            if (ns.StartsWith($"BirraPoint.Api.Modules.{module}.", StringComparison.Ordinal))
            {
                return schema;
            }
        }

        throw new InvalidOperationException($"{entity.ClrType} belongs to no known module.");
    }

    [Fact]
    public void EveryEntityLivesInItsOwnersSchema()
    {
        using var db = NewDb();

        foreach (var entity in db.Model.GetEntityTypes().Where(e => !e.IsOwned()))
        {
            Assert.Equal(ExpectedSchema(entity), entity.GetSchema());
        }
    }

    [Fact]
    public void NoTableIsLeftInPublic()
    {
        using var db = NewDb();

        Assert.All(db.Model.GetEntityTypes(), e =>
        {
            Assert.False(string.IsNullOrEmpty(e.GetSchema()));
            Assert.NotEqual("public", e.GetSchema());
        });
    }

    [Fact]
    public void MigrationHistoryLivesInTheMigrationsSchema()
    {
        using var db = NewDb();

        var script = db.GetService<IHistoryRepository>().GetCreateScript();

        Assert.Contains("migrations.\"__EFMigrationsHistory\"", script, StringComparison.Ordinal);
    }
}
