namespace BuildingBlocks.Keycloak.Admin.Tests;

/// <summary>A building block must stay reusable from any repository (ADR-0035).</summary>
public sealed class KeycloakAdminIndependenceTests
{
    [Fact]
    public void ReferencesNoHostAssembly()
    {
        var references = typeof(IKeycloakAdminClient).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, name => name.StartsWith("BirraPoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProjectFileUsesNoHostMsBuildProperty()
    {
        var project = File.ReadAllText(BlockProjectFile.Find("BuildingBlocks.Keycloak.Admin"));

        Assert.DoesNotContain("BirraPoint", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicSurfaceLivesInTheBlockNamespace()
    {
        var namespaces = typeof(IKeycloakAdminClient).Assembly.GetExportedTypes().Select(t => t.Namespace).Distinct();

        Assert.Equal(["BuildingBlocks.Keycloak.Admin"], namespaces);
    }

    [Fact]
    public void PublicSurfaceIsTheContractOptionsAndRegistrationOnly()
    {
        var exported = typeof(IKeycloakAdminClient).Assembly.GetExportedTypes().Select(t => t.Name).Order(StringComparer.Ordinal);

        Assert.Equal(
            ["IKeycloakAdminClient", "KeycloakAdminOptions", "KeycloakAdminServiceCollectionExtensions"],
            exported);
    }

    [Fact]
    public void CarriesNoHostSpecificDefaults()
    {
        var options = new KeycloakAdminOptions();

        Assert.Null(options.Authority);
        Assert.Null(options.AdminClientId);
        Assert.Null(options.AdminClientSecret);
        Assert.Null(options.DevelopmentPlaceholderSecret);
    }
}
