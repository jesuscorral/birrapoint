using System.Reflection;
using BirraPoint.Common.Domain;
using BirraPoint.Contracts.Catalog;

namespace BirraPoint.Api.UnitTests.Architecture;

/// <summary>FR-102/FR-103: Common and Contracts are leaves of the graph; migrated modules reference neither another module nor the host.</summary>
public class ProjectGraphTests
{
    private static IEnumerable<string> ReferencesOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!);

    [Fact]
    public void CommonReferencesNoBirraPointAssembly() =>
        Assert.Empty(ProjectGraphRules.CommonOffenders(ReferencesOf(typeof(Entity).Assembly)));

    [Fact]
    public void ContractsReferenceOnlyCommonAndBuildingBlocks() =>
        Assert.Empty(ProjectGraphRules.ContractsOffenders(ReferencesOf(typeof(IStyleCatalog).Assembly)));

    [Fact]
    public void AssembliesAreNamedAsTheProjectGraphExpects()
    {
        Assert.Equal(ProjectGraphRules.Common, typeof(Entity).Assembly.GetName().Name);
        Assert.Equal(ProjectGraphRules.Contracts, typeof(IStyleCatalog).Assembly.GetName().Name);
    }

    [Fact]
    public void MigratedModulesReferenceNoOtherModuleAndNotTheHost()
    {
        var offenders = ArchitectureScope.MigratedModules
            .SelectMany(module =>
            {
                var name = $"BirraPoint.{module}";
                return ProjectGraphRules.ModuleOffenders(name, ReferencesOf(Assembly.Load(name))).Select(r => $"{name} -> {r}");
            })
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void CommonRuleReportsAnyBirraPointReference() =>
        Assert.Equal(["BirraPoint.Catalog"], ProjectGraphRules.CommonOffenders(["BuildingBlocks.Jobs", "BirraPoint.Catalog", "System.Runtime"]));

    [Fact]
    public void ContractsRuleAllowsCommonButReportsModulesAndHost() =>
        Assert.Equal(
            ["BirraPoint.Api", "BirraPoint.Judging"],
            ProjectGraphRules.ContractsOffenders(["BirraPoint.Common", "BuildingBlocks.Messaging", "BirraPoint.Judging", "BirraPoint.Api"]));

    [Fact]
    public void ModuleRuleReportsAnotherModuleAndTheHostButNotItself() =>
        Assert.Equal(
            ["BirraPoint.Api", "BirraPoint.Organization"],
            ProjectGraphRules.ModuleOffenders(
                "BirraPoint.Judging",
                ["BirraPoint.Judging", "BirraPoint.Common", "BirraPoint.Contracts", "BirraPoint.Organization", "BirraPoint.Api"]));

    [Fact]
    public void ModuleRuleAcceptsCommonContractsAndBuildingBlocks() =>
        Assert.Empty(ProjectGraphRules.ModuleOffenders("BirraPoint.Judging", ["BirraPoint.Common", "BirraPoint.Contracts", "BuildingBlocks.Jobs"]));
}
