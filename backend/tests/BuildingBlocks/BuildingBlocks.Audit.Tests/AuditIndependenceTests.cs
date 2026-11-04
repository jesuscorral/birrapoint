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
    public void PublicSurfaceLivesInTheBlockNamespace()
    {
        var namespaces = typeof(IAuditWriter).Assembly.GetExportedTypes().Select(t => t.Namespace).Distinct();

        Assert.Equal(["BuildingBlocks.Audit"], namespaces);
    }
}
