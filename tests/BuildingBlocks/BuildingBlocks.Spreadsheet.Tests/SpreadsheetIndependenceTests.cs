namespace BuildingBlocks.Spreadsheet.Tests;

/// <summary>A building block must stay reusable from any repository (ADR-0035).</summary>
public sealed class SpreadsheetIndependenceTests
{
    [Fact]
    public void ReferencesNoHostAssembly()
    {
        var references = typeof(SpreadsheetReader).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, name => name.StartsWith("BirraPoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProjectFileUsesNoHostMsBuildProperty()
    {
        var project = File.ReadAllText(BlockProjectFile.Find("BuildingBlocks.Spreadsheet"));

        Assert.DoesNotContain("BirraPoint", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicSurfaceLivesInTheBlockNamespace()
    {
        var namespaces = typeof(SpreadsheetReader).Assembly.GetExportedTypes().Select(t => t.Namespace).Distinct();

        Assert.Equal(["BuildingBlocks.Spreadsheet"], namespaces);
    }
}
