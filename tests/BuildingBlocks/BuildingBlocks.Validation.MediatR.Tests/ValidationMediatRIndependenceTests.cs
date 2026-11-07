namespace BuildingBlocks.Validation.MediatR.Tests;

/// <summary>A building block must stay reusable from any repository (ADR-0035).</summary>
public sealed class ValidationMediatRIndependenceTests
{
    [Fact]
    public void ReferencesNoHostAssembly()
    {
        var references = typeof(MediatRExtensions).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, name => name.StartsWith("BirraPoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProjectFileUsesNoHostMsBuildProperty()
    {
        var project = File.ReadAllText(BlockProjectFile.Find("BuildingBlocks.Validation.MediatR"));

        Assert.DoesNotContain("BirraPoint", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicSurfaceLivesInTheBlockNamespace()
    {
        var namespaces = typeof(MediatRExtensions).Assembly.GetExportedTypes().Select(t => t.Namespace).Distinct();

        Assert.Equal(["BuildingBlocks.Validation.MediatR"], namespaces);
    }

    [Fact]
    public void PublicSurfaceIsTheRegistrationAndMappingOnly()
    {
        var exported = typeof(MediatRExtensions).Assembly.GetExportedTypes().Select(t => t.Name).Order(StringComparer.Ordinal);

        Assert.Equal(["MediatRExtensions", "ValidationMappingExtensions", "ValidationProblemOptions"], exported);
    }
}
