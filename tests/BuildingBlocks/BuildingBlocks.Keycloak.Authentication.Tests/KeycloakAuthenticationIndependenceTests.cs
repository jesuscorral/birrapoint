namespace BuildingBlocks.Keycloak.Authentication.Tests;

/// <summary>A building block must stay reusable from any repository (ADR-0035).</summary>
public sealed class KeycloakAuthenticationIndependenceTests
{
    [Fact]
    public void ReferencesNoHostAssembly()
    {
        var references = typeof(KeycloakJwtBearerOptions).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, name => name.StartsWith("BirraPoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProjectFileUsesNoHostMsBuildProperty()
    {
        var project = File.ReadAllText(BlockProjectFile.Find("BuildingBlocks.Keycloak.Authentication"));

        Assert.DoesNotContain("BirraPoint", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicSurfaceLivesInTheBlockNamespace()
    {
        var namespaces = typeof(KeycloakJwtBearerOptions).Assembly.GetExportedTypes().Select(t => t.Namespace).Distinct();

        Assert.Equal(["BuildingBlocks.Keycloak.Authentication"], namespaces);
    }

    [Fact]
    public void PublicSurfaceIsTheOptionsAndRegistrationOnly()
    {
        var exported = typeof(KeycloakJwtBearerOptions).Assembly.GetExportedTypes().Select(t => t.Name).Order(StringComparer.Ordinal);

        Assert.Equal(["KeycloakAuthenticationExtensions", "KeycloakJwtBearerOptions"], exported);
    }

    [Fact]
    public void CarriesNoHostSpecificDefaults()
    {
        var options = new KeycloakJwtBearerOptions();

        Assert.Null(options.Authority);
        Assert.Null(options.Audience);
        Assert.True(options.RequireHttpsMetadata);
        Assert.Empty(options.TokenQueryPaths);
    }
}
