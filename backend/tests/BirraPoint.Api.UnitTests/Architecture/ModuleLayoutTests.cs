using System.Reflection;
using BirraPoint.Api.Domain;

namespace BirraPoint.Api.UnitTests.Architecture;

public class ModuleLayoutTests
{
    private static readonly Assembly Api = typeof(Entity).Assembly;

    private static readonly string[] Modules = ["Organization", "Judging", "Publication"];

    private static bool IsCompilerGenerated(Type t) =>
        t.Name.StartsWith('<') || t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false);

    private static IEnumerable<Type> ApiTypes() =>
        Api.GetTypes().Where(t => !IsCompilerGenerated(t) && t.Namespace is not null);

    [Fact]
    public void ModuleNamespacesUseAKnownModule()
    {
        const string prefix = "BirraPoint.Api.Modules.";
        var offenders = ApiTypes()
            .Where(t => t.Namespace!.StartsWith(prefix, StringComparison.Ordinal))
            .Where(t => !Modules.Contains(t.Namespace![prefix.Length..].Split('.')[0]))
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
    [InlineData("BjcpStyle", "Organization")]
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
        var matches = ApiTypes().Where(t => t.Name == typeName && t.Namespace!.EndsWith(".Domain", StringComparison.Ordinal)).ToList();

        Assert.Contains(matches, t => t.Namespace == expected);
    }
}
