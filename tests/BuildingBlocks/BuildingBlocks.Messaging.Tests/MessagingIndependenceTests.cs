namespace BuildingBlocks.Messaging.Tests;

/// <summary>A building block must stay reusable from any repository: it references no host assembly and no
/// other building block (FR-087).</summary>
public class MessagingIndependenceTests
{
    [Fact]
    public void ReferencesNoHostAssemblyAndNoOtherBlock()
    {
        var references = typeof(IIntegrationEventPublisher).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.DoesNotContain(references, name => name.StartsWith("BirraPoint", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name =>
            name.StartsWith("BuildingBlocks.", StringComparison.Ordinal)
            && !name.Equals("BuildingBlocks.Messaging", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectFileUsesNoHostMsBuildProperty()
    {
        var project = File.ReadAllText(BlockProjectFile.Find("BuildingBlocks.Messaging"));

        Assert.DoesNotContain("BirraPoint", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BuildingBlocks.Jobs", project, StringComparison.Ordinal);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicSurfaceLivesInTheBlockNamespace()
    {
        var namespaces = typeof(IIntegrationEventPublisher).Assembly.GetExportedTypes().Select(t => t.Namespace).Distinct();

        Assert.Equal(["BuildingBlocks.Messaging"], namespaces);
    }
}
