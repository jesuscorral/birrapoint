using System.Reflection;
using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.UnitTests.Architecture;

public class ModuleLayoutTests
{
    private static readonly Assembly Api = typeof(Entity).Assembly;

    private static readonly string[] Modules = ["Organization", "Catalog", "Judging", "Publication"];

    private static bool IsCompilerGenerated(Type t) =>
        t.Name.StartsWith('<') || t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false);

    private static IEnumerable<Type> ApiTypes() =>
        Api.GetTypes().Where(t => !IsCompilerGenerated(t) && t.Namespace is not null);

    [Fact]
    public void ModuleNamespacesUseAKnownModule()
    {
        const string root = "BirraPoint.Api.Modules";
        const string prefix = root + ".";
        var offenders = ApiTypes()
            .Where(t => t.Namespace == root || t.Namespace!.StartsWith(prefix, StringComparison.Ordinal))
            .Where(t => t.Namespace == root || !Modules.Contains(t.Namespace![prefix.Length..].Split('.')[0]))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SharedDomainNamespaceOnlyHoldsTheSharedKernel()
    {
        string[] allowed = ["Entity", "AuditLog", "DispatchJob", "DispatchJobStatus", "DispatchJobType", "ITimestamped"];
        var actual = ApiTypes()
            .Where(t => t.Namespace == "BirraPoint.Api.Domain")
            .Select(t => t.Name)
            .Order()
            .ToArray();

        Assert.Equal(allowed.Order().ToArray(), actual);
    }

    [Fact]
    public void LegacyFeaturesRootIsGone()
    {
        var offenders = ApiTypes()
            .Where(t => t.Namespace == "BirraPoint.Api.Features"
                || t.Namespace!.StartsWith("BirraPoint.Api.Features.", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("Organizer", "Organization")]
    [InlineData("Competition", "Organization")]
    [InlineData("CompetitionState", "Organization")]
    [InlineData("CompetitionCategory", "Organization")]
    [InlineData("CompetitionCategoryStyle", "Organization")]
    [InlineData("BjcpStyle", "Catalog")]
    [InlineData("Participant", "Organization")]
    [InlineData("BeerEntry", "Organization")]
    [InlineData("EntryCollaborator", "Organization")]
    [InlineData("Judge", "Organization")]
    [InlineData("Invitation", "Organization")]
    [InlineData("InvitationStatus", "Organization")]
    [InlineData("TastingTable", "Judging")]
    [InlineData("TableState", "Judging")]
    [InlineData("TableJudge", "Judging")]
    [InlineData("TableSample", "Judging")]
    [InlineData("Evaluation", "Judging")]
    [InlineData("EvaluationStatus", "Judging")]
    [InlineData("DiscrepancyAlert", "Judging")]
    [InlineData("DiscrepancyStatus", "Judging")]
    [InlineData("GeneratedScoreSheet", "Publication")]
    [InlineData("ResultsArchive", "Publication")]
    public void EntityLivesInItsModuleDomainNamespace(string typeName, string module)
    {
        var expected = $"BirraPoint.Api.Modules.{module}.Domain";
        var matches = ApiTypes()
            .Where(t => t.Name == typeName && IsDomainNamespace(t.Namespace!))
            .ToList();

        var type = Assert.Single(matches);
        Assert.Equal(expected, type.Namespace);
    }

    private static bool IsDomainNamespace(string ns) =>
        ns == "BirraPoint.Api.Domain"
        || (ns.StartsWith("BirraPoint.Api.Modules.", StringComparison.Ordinal) && ns.EndsWith(".Domain", StringComparison.Ordinal));

    [Theory]
    [InlineData("BeerImportBatch")]
    [InlineData("BeerImportBatchStatus")]
    [InlineData("BeerImportRow")]
    [InlineData("BeerImportRowStatus")]
    [InlineData("JudgeImportBatch")]
    [InlineData("JudgeImportBatchStatus")]
    [InlineData("JudgeImportRow")]
    [InlineData("JudgeImportRowStatus")]
    public void ImportStagingTypesLiveInTheOrganizationImportFeature(string typeName)
    {
        var type = Assert.Single(ApiTypes(), t => t.Name == typeName);

        Assert.Equal("BirraPoint.Api.Modules.Organization.Features.Import", type.Namespace);
    }

    [Fact]
    public void EntityConfigurationsLiveBesideTheirEntityModule()
    {
        const string modulesPrefix = "BirraPoint.Api.Modules.";
        var offenders = new List<string>();

        foreach (var config in ApiTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
        {
            var entityTypes = config.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>))
                .Select(i => i.GetGenericArguments()[0]);

            foreach (var entity in entityTypes)
            {
                var entityNs = entity.Namespace!;
                string? expected = null;
                if (entityNs.StartsWith(modulesPrefix, StringComparison.Ordinal))
                {
                    expected = $"{modulesPrefix}{entityNs[modulesPrefix.Length..].Split('.')[0]}.Persistence";
                }
                else if (entityNs == "BirraPoint.Api.Domain")
                {
                    expected = "BirraPoint.Api.Common.Persistence.Configurations";
                }

                if (expected is null || config.Namespace != expected)
                {
                    offenders.Add($"{config.FullName} (for {entity.FullName}, expected namespace {expected ?? "<none>"})");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryEntityLivesInADomainNamespaceOrTheImportFeature()
    {
        const string importNs = "BirraPoint.Api.Modules.Organization.Features.Import";
        var dbContext = Api.GetType("BirraPoint.Api.Common.Persistence.AppDbContext", throwOnError: true)!;
        var dbSetEntities = dbContext
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition().Name.StartsWith("DbSet", StringComparison.Ordinal))
            .Select(p => p.PropertyType.GetGenericArguments()[0]);
        var subclasses = ApiTypes().Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(Entity)));

        var allowedDomains = Modules.Select(m => $"BirraPoint.Api.Modules.{m}.Domain")
            .Append("BirraPoint.Api.Domain")
            .Append(importNs)
            .ToHashSet(StringComparer.Ordinal);

        var offenders = subclasses.Concat(dbSetEntities)
            .Distinct()
            .Where(t => !allowedDomains.Contains(t.Namespace!))
            .Select(t => t.FullName)
            .ToList();

        Assert.Empty(offenders);
    }
}
