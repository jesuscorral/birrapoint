using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Common.Persistence.Audit;
using BirraPoint.Api.Common.Persistence.Jobs;
using BirraPoint.Api.Modules.Catalog.Persistence;
using BirraPoint.Api.Modules.Judging.Persistence;
using BirraPoint.Api.Modules.Organization.Persistence;
using BirraPoint.Api.Modules.Publication.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BirraPoint.Api.UnitTests.Architecture;

/// <summary>Each of the six contexts maps only its own schema, its own module's entities (or its building block's), has
/// no foreign key leaving its model and keeps its migration history in its own schema (FR-092, FR-093, FR-097, FR-099).
/// Models are built without a database connection.</summary>
public sealed class DbContextModelTests
{
    private static BirraPointDbContext Build(Type contextType)
    {
        var optionsType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(optionsType)!;
        builder.UseNpgsql("Host=localhost");
        return (BirraPointDbContext)Activator.CreateInstance(contextType, builder.Options, null)!;
    }

    public static TheoryData<Type, string, string> Contexts => new()
    {
        { typeof(JobsDbContext), "jobs", "BuildingBlocks.Jobs" },
        { typeof(AuditDbContext), "audit", "BuildingBlocks.Audit" },
        { typeof(CatalogDbContext), "catalog", "BirraPoint.Api.Modules.Catalog" },
        { typeof(OrganizationDbContext), "organization", "BirraPoint.Api.Modules.Organization" },
        { typeof(JudgingDbContext), "judging", "BirraPoint.Api.Modules.Judging" },
        { typeof(PublicationDbContext), "publication", "BirraPoint.Api.Modules.Publication" },
    };

    [Theory]
    [MemberData(nameof(Contexts))]
    public void EveryEntityLivesInTheContextsSchema(Type contextType, string schema, string ownNamespace)
    {
        _ = ownNamespace;
        using var db = Build(contextType);

        Assert.Equal(schema, db.Schema);
        Assert.All(db.Model.GetEntityTypes().Where(e => !e.IsOwned()), e => Assert.Equal(schema, e.GetSchema()));
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void EveryClrTypeBelongsToTheContextsModuleOrBuildingBlock(Type contextType, string schema, string ownNamespace)
    {
        _ = schema;
        using var db = Build(contextType);

        var offenders = db.Model.GetEntityTypes()
            .Select(e => e.ClrType)
            .Where(t => !Allowed(t, ownNamespace))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void NoForeignKeyLeavesTheModel(Type contextType, string schema, string ownNamespace)
    {
        _ = ownNamespace;
        using var db = Build(contextType);

        var offenders = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys())
            .Where(fk => fk.PrincipalEntityType.GetSchema() != schema || fk.PrincipalEntityType.Model != db.Model)
            .Select(fk => $"{fk.DeclaringEntityType.ClrType.Name} -> {fk.PrincipalEntityType.ClrType.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void MigrationHistoryLivesInTheContextsOwnSchema(Type contextType, string schema, string ownNamespace)
    {
        _ = ownNamespace;
        using var db = Build(contextType);

        var script = ((IInfrastructure<IServiceProvider>)db).Instance.GetService(typeof(IHistoryRepository)) as IHistoryRepository;

        Assert.NotNull(script);
        Assert.Contains($"{schema}.\"__EFMigrationsHistory\"", script.GetCreateScript(), StringComparison.Ordinal);
    }

    /// <summary>A module context also maps the messaging block's outbox/inbox rows; the building-block contexts map
    /// only their own block.</summary>
    private static bool Allowed(Type type, string ownNamespace)
    {
        var ns = type.Namespace ?? string.Empty;
        return IsIn(ns, ownNamespace)
            || (ownNamespace.StartsWith("BirraPoint.Api.Modules.", StringComparison.Ordinal) && IsIn(ns, "BuildingBlocks.Messaging"));
    }

    private static bool IsIn(string ns, string prefix) =>
        ns.Equals(prefix, StringComparison.Ordinal) || ns.StartsWith(prefix + ".", StringComparison.Ordinal);
}
