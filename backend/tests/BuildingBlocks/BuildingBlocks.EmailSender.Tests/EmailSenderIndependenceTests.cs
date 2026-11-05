namespace BuildingBlocks.EmailSender.Tests;

/// <summary>A building block must stay reusable from any repository (ADR-0035).</summary>
public sealed class EmailSenderIndependenceTests
{
    [Fact]
    public void ReferencesNoHostAssembly()
    {
        var references = typeof(IEmailSender).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, name => name.StartsWith("BirraPoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProjectFileUsesNoHostMsBuildProperty()
    {
        // Assembly references cannot show an MSBuild coupling such as $(BirraPointTargetFramework),
        // which would leave TargetFramework empty outside this repository.
        var project = File.ReadAllText(BlockProjectFile.Find("BuildingBlocks.EmailSender"));

        Assert.DoesNotContain("BirraPoint", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicSurfaceLivesInTheBlockNamespace()
    {
        var namespaces = typeof(IEmailSender).Assembly.GetExportedTypes().Select(t => t.Namespace).Distinct();

        Assert.Equal(["BuildingBlocks.EmailSender"], namespaces);
    }

    [Fact]
    public void PublicSurfaceIsTheContractOptionsAndRegistrationOnly()
    {
        var exported = typeof(IEmailSender).Assembly.GetExportedTypes().Select(t => t.Name).Order(StringComparer.Ordinal);

        Assert.Equal(
            ["EmailAttachment", "EmailSenderServiceCollectionExtensions", "IEmailSender", "SmtpOptions"],
            exported);
    }

    [Fact]
    public void CarriesNoHostSpecificDefaults()
    {
        var options = new SmtpOptions();

        Assert.Null(options.From);
        Assert.Null(options.Host);
        Assert.Equal(0, options.Port);
    }
}
