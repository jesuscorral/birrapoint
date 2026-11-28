using System.Reflection;
using MediatR;

namespace BirraPoint.Api.UnitTests.Architecture;

/// <summary>FR-105/FR-109: every request is a command or a query, queries only read, aggregates protect their state.</summary>
public class CommandQueryTests
{
    private static IEnumerable<Assembly> MigratedAssemblies() =>
        ArchitectureScope.MigratedModules.Select(module => Assembly.Load($"BirraPoint.{module}"));

    [Fact]
    public void EveryRequestOfAMigratedModuleIsExactlyOneOfCommandOrQuery() =>
        Assert.Empty(CommandQueryRules.RequestsWithoutExactlyOneMarker(MigratedAssemblies().SelectMany(a => a.GetTypes())));

    [Fact]
    public void QueryHandlersOfMigratedModulesOnlyRead()
    {
        var violations = ArchitectureScope.MigratedModules
            .SelectMany(module => QueryHandlerFiles(ArchitectureScope.ModuleFolder(module)))
            .SelectMany(file => CommandQueryRules.QueryHandlerViolations(File.ReadAllText(file)).Select(v => $"{file}: {v}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void ProtectedAggregatesExposeNoPublicSetter()
    {
        var types = MigratedAssemblies().SelectMany(a => a.GetTypes()).ToList();
        var missing = ArchitectureScope.ProtectedAggregates.Where(name => types.TrueForAll(t => t.FullName != name)).ToList();
        var setters = types.Where(t => ArchitectureScope.ProtectedAggregates.Contains(t.FullName!)).SelectMany(CommandQueryRules.PublicSetters).ToList();

        Assert.Empty(missing);
        Assert.Empty(setters);
    }

    private static IEnumerable<string> QueryHandlerFiles(string moduleFolder) =>
        Directory.EnumerateFiles(moduleFolder, "Request.cs", SearchOption.AllDirectories)
            .Where(request => File.ReadAllText(request).Contains("IQuery<", StringComparison.Ordinal))
            .Select(request => Path.Combine(Path.GetDirectoryName(request)!, "Handler.cs"))
            .Where(File.Exists);

    [Fact]
    public void ARequestWithoutAMarkerIsReported() =>
        Assert.Equal([typeof(PlainRequest).FullName!], CommandQueryRules.RequestsWithoutExactlyOneMarker([typeof(PlainRequest), typeof(FixtureCommand), typeof(FixtureQuery)]));

    [Fact]
    public void ARequestWithBothMarkersIsReported() =>
        Assert.Equal([typeof(BothMarkers).FullName!], CommandQueryRules.RequestsWithoutExactlyOneMarker([typeof(BothMarkers), typeof(FixtureCommand)]));

    [Theory]
    [InlineData("await db.SaveChangesAsync(ct);")]
    [InlineData("var x = await db.Tables.FindAsync(id);")]
    [InlineData("db.Attach(entity);")]
    [InlineData("FROM t FOR UPDATE")]
    [InlineData("db.Tables.Update(row);")]
    public void AQueryHandlerThatWritesOrLocksIsReported(string body) =>
        Assert.NotEmpty(CommandQueryRules.QueryHandlerViolations($"db.Tables.AsNoTracking(); {body}"));

    [Fact]
    public void AQueryHandlerReadingWithoutAsNoTrackingIsReported() =>
        Assert.Contains("AsNoTracking", Assert.Single(CommandQueryRules.QueryHandlerViolations("await db.Tables.ToListAsync(ct);")), StringComparison.Ordinal);

    [Fact]
    public void AReadOnlyQueryHandlerPasses()
    {
        Assert.Empty(CommandQueryRules.QueryHandlerViolations("await db.Tables.AsNoTracking().Select(t => t.Id).ToListAsync(ct);"));
        Assert.Empty(CommandQueryRules.QueryHandlerViolations("return await styles.ListAsync(ct);"));
    }

    [Fact]
    public void OnlyAPublicNonInitSetterIsReportedOnAnAggregate() =>
        Assert.Equal([$"{typeof(FixtureAggregate).FullName}.Name"], CommandQueryRules.PublicSetters(typeof(FixtureAggregate)));
}
