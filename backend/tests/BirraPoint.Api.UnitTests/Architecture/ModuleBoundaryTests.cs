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
/// Module allow-list = temporary debt (shrinks; ADR-0033 phase 3 replaces each group with an Organization contract).
/// Kernel exceptions = permanent (ADR-0034).
/// </summary>
public class ModuleBoundaryTests
{
    private const string ModulesNamespace = "BirraPoint.Api.Modules";

    private static readonly string[] KernelFolders = ["Common", "Domain", "Realtime"];

    private const string JudgingReadsOrganization =
        "Judging reads Organization competition/entry/judge data directly until its ADR-0033 phase 3 contract exists.";

    private const string PublicationReadsOrganization =
        "Publication reads Organization competition/entry/participant data for results delivery until its ADR-0033 phase 3 contract exists.";

    /// <summary>A tolerated violation. <paramref name="DbSets"/> (optional) narrows it to exactly those DbSet
    /// members, so a file that must lose some of its reads cannot hide behind a broader entry; using directives
    /// of the same module pair stay tolerated.</summary>
    private sealed record Debt(string File, string Module, string Reason, string[]? DbSets = null);

    /// <summary>Violations that remain after the contracts are in place (phase 3 removes them). A stale entry
    /// (no violation any more) fails the build, so this list can only shrink.</summary>
    private static readonly Debt[] ModuleAllowList =
    [
        new("Modules/Judging/Features/Evaluations/CommonComponents/DiscrepancyViewBuilder.cs", "Organization", JudgingReadsOrganization, ["BeerEntries", "Judges"]),
        new("Modules/Judging/Features/Evaluations/Features/AdjustEvaluation/AdjustEvaluationCommand.cs", "Organization", JudgingReadsOrganization, ["BeerEntries"]),
        new("Modules/Judging/Features/Evaluations/Features/CloseTable/CloseTableCommand.cs", "Organization", JudgingReadsOrganization, ["BeerEntries"]),
        new("Modules/Judging/Features/Evaluations/Features/SubmitEvaluation/SubmitEvaluationCommand.cs", "Organization", JudgingReadsOrganization, ["BeerEntries"]),
        new("Modules/Judging/Features/Tables/CommonComponents/CommonTableValidationRules.cs", "Organization", JudgingReadsOrganization, ["BeerEntries", "Judges"]),
        new("Modules/Judging/Features/Tables/CommonComponents/TableProjector.cs", "Organization", JudgingReadsOrganization, ["BeerEntries", "CompetitionCategories", "Judges"]),
        new("Modules/Judging/Features/Tables/Features/UpdateTable/LiveJudgeRemoval.cs", "Organization", JudgingReadsOrganization, ["BeerEntries", "Judges"]),
        new("Modules/Judging/Features/TastingOrder/CommonComponents/JudgeSampleProjector.cs", "Organization", JudgingReadsOrganization, ["BeerEntries"]),
        new("Modules/Judging/Features/TastingOrder/CommonComponents/JudgeTableAccess.cs", "Organization", JudgingReadsOrganization, []),
        new("Modules/Judging/Features/TastingOrder/Features/FixOrder/FixOrderCommand.cs", "Organization", JudgingReadsOrganization, ["Judges"]),
        new("Modules/Judging/Features/TastingOrder/Features/GetMyTables/GetMyTablesQuery.cs", "Organization", JudgingReadsOrganization, ["Judges"]),
        new("Modules/Judging/Features/TastingOrder/Features/GetTableJudges/GetTableJudgesQuery.cs", "Organization", JudgingReadsOrganization, ["Judges"]),
        new("Modules/Judging/Features/Monitoring/Features/GetEntryEvaluations/GetEntryEvaluationsQuery.cs", "Organization", JudgingReadsOrganization, ["BeerEntries"]),
        new("Modules/Judging/Features/Tables/CommonComponents/TableAssignmentApplier.cs", "Organization", "Flags and unflags BeerEntry.NotValidForBos directly; conflict data comes from IConflictOfInterestChecker.", ["BeerEntries"]),
        new("Modules/Publication/Features/Dispatch/Features/DispatchScoreSheet/BundleZipHandler.cs", "Organization", PublicationReadsOrganization, ["BeerEntries", "Participants"]),
        new("Modules/Publication/Features/Dispatch/Features/DispatchScoreSheet/SendResultEmailHandler.cs", "Organization", PublicationReadsOrganization, ["BeerEntries", "Participants"]),
        new("Modules/Publication/Features/Dispatch/Features/DispatchScoreSheet/GeneratePdfsHandler.cs", "Organization", PublicationReadsOrganization, ["BeerEntries"]),
        new("Modules/Publication/Features/Dispatch/Features/GetDispatchStatus/GetDispatchStatusQuery.cs", "Organization", PublicationReadsOrganization, ["Participants"]),
    ];

    /// <summary>Permanent kernel → module exceptions (ADR-0034), by path prefix relative to the API project.</summary>
    private static readonly (string PathPrefix, string Reason)[] KernelExceptions =
    [
        ("Common/Auth/", "Identity resolvers and CurrentUser map Keycloak subjects to Organization organizers/judges."),
        ("Realtime/", "Hub group authorization checks competition ownership and judge table membership."),
        ("Common/Persistence/", "AppDbContext, entity configurations and migrations compose every module's model."),
    ];

    [Fact]
    public void ModulesReferenceOtherModulesOnlyThroughContractsOrAllowList()
    {
        var undeclared = ScanModules()
            .Where(v => !IsTolerated(v))
            .Select(Describe)
            .ToList();

        Assert.True(undeclared.Count == 0, "Undeclared cross-module references:\n" + string.Join('\n', undeclared));
    }

    [Fact]
    public void ModuleAllowListHasNoStaleEntries()
    {
        var actual = ScanModules().ToDictionary(v => (v.File, v.Module));
        var stale = ModuleAllowList
            .Where(a => !actual.TryGetValue((a.File, a.Module), out var v)
                || (a.DbSets is not null && a.DbSets.Any(name => !v.Members.Contains(name))))
            .Select(a => $"{a.File} -> {a.Module}")
            .ToList();

        Assert.True(stale.Count == 0, "Allow-list entries without a violation (remove them):\n" + string.Join('\n', stale));
    }

    [Fact]
    public void ModuleAllowListEntriesAreUniqueAndExplained()
    {
        Assert.Empty(ModuleAllowList.GroupBy(a => (a.File, a.Module)).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.Empty(ModuleAllowList.Where(a => string.IsNullOrWhiteSpace(a.Reason)).Select(a => a.File));
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

    private static string Describe(Violation v) => $"{v.File} -> {v.Module}: {string.Join("; ", v.Members.Order(StringComparer.Ordinal))}";

    private static bool IsTolerated(Violation v)
    {
        var debt = ModuleAllowList.SingleOrDefault(a => a.File == v.File && a.Module == v.Module);
        return debt is not null
            && (debt.DbSets is null
                || v.Members.Where(m => !ModuleBoundaryScanner.IsReferenceMember(m)).All(debt.DbSets.Contains));
    }

    private static string ApiRoot { get; } = FindApiRoot();

    private static string FindApiRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "backend", "BirraPoint.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "backend", "src", "BirraPoint.Api");
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
