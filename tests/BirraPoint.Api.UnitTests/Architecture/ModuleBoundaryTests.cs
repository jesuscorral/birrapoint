using System.Reflection;
using BirraPoint.Api.Domain;

namespace BirraPoint.Api.UnitTests.Architecture;

/// <summary>
/// ADR-0033/ADR-0034 boundary: a module may only reference another module through that module's
/// <c>Contracts</c> namespace, and may not touch another module's <c>DbSet</c>s. Source-scan test
/// (single assembly, so reflection cannot see usings).
///
/// Heuristics, deliberately simple:
/// <list type="bullet">
/// <item>Using directives (plain, <c>global</c>, <c>static</c> and alias): <c>using BirraPoint.Api.Modules.&lt;Y&gt;.Contracts…;</c>
/// is allowed, any other namespace of module Y is a violation from module X ≠ Y. Fully qualified
/// <c>BirraPoint.Api.Modules.&lt;Y&gt;.…</c> names in code follow the same rule.</item>
/// <item>DbSet access: the DbSet-name → owning-module map is built by reflection over
/// <c>AppDbContext</c> (module = the module segment of the entity's namespace). Only member access on a
/// DbContext-looking receiver (identifier ending in <c>dbContext</c>, <c>db</c>, <c>context</c> or <c>ctx</c>, e.g.
/// <c>_db</c>, <c>appDbContext</c>) is matched, so same-named
/// navigations such as <c>table.Judges</c> are not false positives. <c>Set&lt;T&gt;()</c> and
/// <c>Include</c> navigations are intentionally out of scope.</item>
/// <item>Comments are stripped before matching.</item>
/// <item><c>Modules/&lt;X&gt;/Persistence/</c> is exempt: FK configurations are allowed with the shared DbContext.</item>
/// </list>
///
/// No module-to-module exception exists: ADR-0033 phase 3 replaced every direct read with a contract.
/// Kernel exceptions are permanent (ADR-0034).
/// </summary>
public class ModuleBoundaryTests
{
    private const string ModulesNamespace = "BirraPoint.Api.Modules";

    private static readonly string[] KernelFolders = ["Common", "Domain", "Realtime"];

    /// <summary>Permanent kernel → module exceptions (ADR-0034), by path prefix relative to the API project.</summary>
    private static readonly (string PathPrefix, string Reason)[] KernelExceptions =
    [
        ("Realtime/", "Hub group authorization checks competition ownership and judge table membership."),
        ("Common/Persistence/", "AppDbContext, entity configurations and migrations compose every module's model."),
    ];

    [Fact]
    public void ModulesReferenceOtherModulesOnlyThroughContracts()
    {
        var violations = ScanModules().Select(Describe).ToList();

        Assert.True(violations.Count == 0, "Cross-module references outside Contracts:\n" + string.Join('\n', violations));
    }

    [Fact]
    public void KernelReferencesModulesOnlyThroughDocumentedExceptions()
    {
        var offenders = ScanKernel()
            .Where(v => !KernelExceptions.Any(e => v.File.StartsWith(e.PathPrefix, StringComparison.Ordinal)))
            .Select(Describe)
            .ToList();

        Assert.True(offenders.Count == 0, "Kernel references modules outside the documented exceptions:\n" + string.Join('\n', offenders));
    }

    /// <summary>
    /// Each module owns its domain errors under <c>Modules/&lt;M&gt;/Errors</c> (ADR-0033/0037), so no
    /// module may depend on the host's cross-cutting error namespace. Cross-module use of another
    /// module's <c>Errors</c> namespace is already rejected by the contracts-only rule above.
    /// </summary>
    [Fact]
    public void ModulesDoNotReferenceTheHostErrorsNamespace()
    {
        const string hostErrors = "BirraPoint.Api.Common.Errors";
        var offenders = Directory.GetDirectories(Path.Combine(ApiRoot, "Modules"))
            .SelectMany(dir => SourceFiles($"Modules/{Path.GetFileName(dir)}"))
            .Where(f => f.Source.Contains(hostErrors, StringComparison.Ordinal))
            .Select(f => f.Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0, $"Modules reference {hostErrors}:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static string Describe(Violation v) => $"{v.File} -> {v.Module}: {string.Join("; ", v.Members.Order(StringComparer.Ordinal))}";

    private static string ApiRoot { get; } = FindApiRoot();

    private static string FindApiRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BirraPoint.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "src", "BirraPoint.Api");
    }

    /// <summary>DbSet property name → module owning its entity (by namespace segment after Modules).</summary>
    private static Dictionary<string, string> DbSetOwners()
    {
        var api = typeof(Entity).Assembly;
        var dbContext = api.GetType("BirraPoint.Api.Common.Persistence.AppDbContext", throwOnError: true)!;
        const string prefix = ModulesNamespace + ".";
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var property in dbContext.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (!property.PropertyType.IsGenericType
                || !property.PropertyType.GetGenericTypeDefinition().Name.StartsWith("DbSet", StringComparison.Ordinal))
            {
                continue;
            }

            var ns = property.PropertyType.GetGenericArguments()[0].Namespace;
            if (ns is not null && ns.StartsWith(prefix, StringComparison.Ordinal))
            {
                map[property.Name] = ns[prefix.Length..].Split('.')[0];
            }
        }

        return map;
    }

    private static IEnumerable<(string Relative, string Source)> SourceFiles(string folder) =>
        Directory.EnumerateFiles(Path.Combine(ApiRoot, folder), "*.cs", SearchOption.AllDirectories)
            .Select(path => (Relative: Path.GetRelativePath(ApiRoot, path).Replace('\\', '/'), path))
            .Where(f => !f.Relative.Contains("/obj/", StringComparison.Ordinal) && !f.Relative.Contains("/bin/", StringComparison.Ordinal))
            .Select(f => (f.Relative, File.ReadAllText(f.path)));

    private static List<Violation> ScanModules()
    {
        var dbSets = DbSetOwners();
        var violations = new List<Violation>();

        foreach (var moduleDir in Directory.GetDirectories(Path.Combine(ApiRoot, "Modules")))
        {
            var module = Path.GetFileName(moduleDir);
            foreach (var (relative, source) in SourceFiles($"Modules/{module}"))
            {
                if (relative.StartsWith($"Modules/{module}/Persistence/", StringComparison.Ordinal))
                {
                    continue;
                }

                violations.AddRange(ModuleBoundaryScanner.Scan(relative, source, module, dbSets));
            }
        }

        return Distinct(violations);
    }

    private static List<Violation> ScanKernel()
    {
        var dbSets = DbSetOwners();
        var violations = KernelFolders
            .Where(folder => Directory.Exists(Path.Combine(ApiRoot, folder)))
            .SelectMany(SourceFiles)
            .SelectMany(f => ModuleBoundaryScanner.Scan(f.Relative, f.Source, ownModule: null, dbSets))
            .ToList();

        return Distinct(violations);
    }

    private static List<Violation> Distinct(List<Violation> violations) =>
        violations
            .GroupBy(v => (v.File, v.Module))
            .Select(g => new Violation(g.Key.File, g.Key.Module, g.SelectMany(v => v.Members).ToHashSet(StringComparer.Ordinal)))
            .OrderBy(v => v.File, StringComparer.Ordinal)
            .ToList();
}
