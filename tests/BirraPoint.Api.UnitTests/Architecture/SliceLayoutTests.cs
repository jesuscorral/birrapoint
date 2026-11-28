namespace BirraPoint.Api.UnitTests.Architecture;

/// <summary>FR-104: slice and area layout of every migrated module, with negative tests on throw-away folders.</summary>
public sealed class SliceLayoutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birrapoint-layout-" + Guid.NewGuid().ToString("N"));

    public SliceLayoutTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void MigratedModulesFollowTheFlatSliceLayout()
    {
        var violations = ArchitectureScope.MigratedModules
            .SelectMany(module => SliceLayoutRules.Violations(ArchitectureScope.ModuleFolder(module)).Select(v => $"{module}/{v}"))
            .ToList();

        Assert.Empty(violations);
    }

    private void Touch(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "// fixture");
    }

    [Fact]
    public void AWellFormedModulePasses()
    {
        foreach (var file in new[] { "Tables/Setup.cs", "Tables/Create/Endpoint.cs", "Tables/Create/Request.cs", "Tables/Create/Handler.cs", "Tables/Create/Response.cs", "Tables/Shared/Helper.cs", "Domain/Table.cs" })
        {
            Touch(file);
        }

        Assert.Empty(SliceLayoutRules.Violations(_root));
    }

    [Fact]
    public void ASliceMissingItsHandlerIsReported()
    {
        Touch("Tables/Setup.cs");
        Touch("Tables/Create/Endpoint.cs");
        Touch("Tables/Create/Request.cs");

        Assert.Contains(SliceLayoutRules.Violations(_root), v => v.Contains("missing Handler.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void AnExtraFileInASliceIsReported()
    {
        Touch("Tables/Setup.cs");
        foreach (var file in new[] { "Endpoint.cs", "Request.cs", "Handler.cs", "CreateValidator.cs" })
        {
            Touch($"Tables/Create/{file}");
        }

        Assert.Contains(SliceLayoutRules.Violations(_root), v => v.Contains("unexpected CreateValidator.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAreaWithoutSetupIsReported()
    {
        foreach (var file in new[] { "Endpoint.cs", "Request.cs", "Handler.cs" })
        {
            Touch($"Tables/Create/{file}");
        }

        Assert.Contains(SliceLayoutRules.Violations(_root), v => v.Contains("no Setup.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void ASetupInsideASliceIsReported()
    {
        Touch("Tables/Setup.cs");
        foreach (var file in new[] { "Endpoint.cs", "Request.cs", "Handler.cs", "Setup.cs" })
        {
            Touch($"Tables/Create/{file}");
        }

        Assert.Contains(SliceLayoutRules.Violations(_root), v => v.Contains("belongs to the area", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Features/Create/Handler.cs")]
    [InlineData("Tables/CommonComponents/Helper.cs")]
    [InlineData("Tables/BaseTablesEndpoints.cs")]
    [InlineData("JudgingModuleEndpoints.cs")]
    public void RetiredFoldersAndFileNamesAreReported(string file)
    {
        Touch(file);

        Assert.NotEmpty(SliceLayoutRules.Violations(_root));
    }

    [Fact]
    public void BuildOutputIsIgnored()
    {
        Touch("obj/Debug/Features/Generated.cs");

        Assert.Empty(SliceLayoutRules.Violations(_root));
    }
}
