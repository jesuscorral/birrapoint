namespace BuildingBlocks.Audit.Tests;

/// <summary>A building block must stay reusable from any repository: it references no host assembly.</summary>
public class AuditIndependenceTests
{
    [Fact]
    public void ReferencesNoHostAssembly()
    {
        var references = typeof(IAuditWriter).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, name => name.StartsWith("BirraPoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProjectFileUsesNoHostMsBuildProperty()
    {
        // Assembly references cannot show an MSBuild coupling such as $(BirraPointTargetFramework),
        // which would leave TargetFramework empty outside this repository.
        var project = File.ReadAllText(BlockProjectFile.Find("BuildingBlocks.Audit"));

        Assert.DoesNotContain("BirraPoint", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicSurfaceLivesInTheBlockNamespace()
    {
        var namespaces = typeof(IAuditWriter).Assembly.GetExportedTypes().Select(t => t.Namespace).Distinct();

        Assert.Equal(["BuildingBlocks.Audit"], namespaces);
    }
}
