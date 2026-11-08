namespace BuildingBlocks.Identity.Tests;

/// <summary>A building block must stay reusable from any repository: it references no host assembly.</summary>
public class IdentityIndependenceTests
{
    [Fact]
    public void ReferencesNoHostOrKeycloakAssembly()
    {
        var references = typeof(ICurrentUser).Assembly.GetReferencedAssemblies().Select(a => a.Name!);

        Assert.DoesNotContain(references, name => name.StartsWith("BirraPoint", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name => name.Contains("Keycloak", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProjectFileUsesNoHostMsBuildPropertyAndNoPackage()
    {
        var project = File.ReadAllText(BlockProjectFile.Find("BuildingBlocks.Identity"));

        Assert.DoesNotContain("BirraPoint", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PackageReference", project, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectReference", project, StringComparison.Ordinal);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicSurfaceIsTheCurrentUserContractAndItsRegistration()
    {
        var types = typeof(ICurrentUser).Assembly.GetExportedTypes();

        Assert.All(types, t => Assert.Equal("BuildingBlocks.Identity", t.Namespace));
        Assert.Equivalent(
            new[] { nameof(ICurrentUser), nameof(IdentityServiceCollectionExtensions) },
            types.Select(t => t.Name).Order().ToArray(),
            strict: true);
    }
}
