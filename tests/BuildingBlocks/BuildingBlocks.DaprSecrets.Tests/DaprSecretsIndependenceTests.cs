namespace BuildingBlocks.DaprSecrets.Tests;

/// <summary>A building block must stay reusable from any repository (ADR-0035).</summary>
public sealed class DaprSecretsIndependenceTests
{
    [Fact]
    public void ReferencesNoHostAssembly()
    {
        var references = typeof(DaprSecretsOptions).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, name => name.StartsWith("BirraPoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProjectFileUsesNoHostMsBuildProperty()
    {
        // Assembly references cannot show an MSBuild coupling such as $(BirraPointTargetFramework),
        // which would leave TargetFramework empty outside this repository.
        var project = File.ReadAllText(BlockProjectFile.Find("BuildingBlocks.DaprSecrets"));

        Assert.DoesNotContain("BirraPoint", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicSurfaceLivesInTheBlockNamespace()
    {
        var namespaces = typeof(DaprSecretsOptions).Assembly.GetExportedTypes().Select(t => t.Namespace).Distinct();

        Assert.Equal(["BuildingBlocks.DaprSecrets"], namespaces);
    }

    [Fact]
    public void PublicSurfaceIsTheOptionsAndRegistrationOnly()
    {
        var exported = typeof(DaprSecretsOptions).Assembly.GetExportedTypes().Select(t => t.Name).Order(StringComparer.Ordinal);

        Assert.Equal(["DaprSecret", "DaprSecretsConfigurationExtensions", "DaprSecretsOptions"], exported);
    }

    [Fact]
    public void CarriesNoHostSecrets()
    {
        Assert.Empty(new DaprSecretsOptions().Secrets);
    }
}
