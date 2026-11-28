namespace BirraPoint.Api.UnitTests.Architecture;

/// <summary>The allowed shape of the project graph (FR-102, FR-103), as pure functions over assembly names so each rule can be
/// shown to fail on a fake reference set.</summary>
internal static class ProjectGraphRules
{
    public const string Common = "BirraPoint.Common";
    public const string Contracts = "BirraPoint.Contracts";

    private static readonly string[] Hosts = ["BirraPoint.Api", "BirraPoint.Host"];

    private static readonly string[] ModuleAssemblies =
        ["BirraPoint.Catalog", "BirraPoint.Organization", "BirraPoint.Judging", "BirraPoint.Publication"];

    /// <summary>The shared kernel depends on building blocks only: no BirraPoint assembly at all.</summary>
    public static IReadOnlyList<string> CommonOffenders(IEnumerable<string> references) =>
        references.Where(IsBirraPoint).Order(StringComparer.Ordinal).ToList();

    /// <summary>Contracts see the kernel and building blocks, never a module or the host.</summary>
    public static IReadOnlyList<string> ContractsOffenders(IEnumerable<string> references) =>
        references.Where(r => IsBirraPoint(r) && r != Common).Order(StringComparer.Ordinal).ToList();

    /// <summary>A module references no other module and not the host; the compiler enforces it, this guards the project files.</summary>
    public static IReadOnlyList<string> ModuleOffenders(string module, IEnumerable<string> references) =>
        references
            .Where(r => r != module && (ModuleAssemblies.Contains(r, StringComparer.Ordinal) || Hosts.Contains(r, StringComparer.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static bool IsBirraPoint(string name) => name.StartsWith("BirraPoint.", StringComparison.Ordinal);
}
