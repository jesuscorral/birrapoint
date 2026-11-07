namespace BuildingBlocks.ProblemDetails.Tests;

/// <summary>A building block must stay reusable from any repository (ADR-0035).</summary>
public sealed class ProblemDetailsIndependenceTests
{
    [Fact]
    public void ReferencesNoHostAssembly()
    {
        var references = typeof(ProblemMapping).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, name => name.StartsWith("BirraPoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProjectFileUsesNoHostMsBuildProperty()
    {
        var project = File.ReadAllText(BlockProjectFile.Find("BuildingBlocks.ProblemDetails"));

        Assert.DoesNotContain("BirraPoint", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicSurfaceLivesInTheBlockNamespace()
    {
        var namespaces = typeof(ProblemMapping).Assembly.GetExportedTypes().Select(t => t.Namespace).Distinct();

        Assert.Equal(["BuildingBlocks.ProblemDetails"], namespaces);
    }

    [Fact]
    public void PublicSurfaceIsTheMappingModelBuilderAndRegistrationOnly()
    {
        var exported = typeof(ProblemMapping).Assembly.GetExportedTypes().Select(t => t.Name).Order(StringComparer.Ordinal);

        Assert.Equal(
            ["ProblemDetailsMappingBuilder", "ProblemDetailsServiceCollectionExtensions", "ProblemMapping"],
            exported);
    }
}
