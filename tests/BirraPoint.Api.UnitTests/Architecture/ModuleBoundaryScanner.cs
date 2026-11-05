using System.Text.RegularExpressions;

namespace BirraPoint.Api.UnitTests.Architecture;

/// <summary>Members: <c>using:&lt;namespace below the module&gt;</c>, <c>fqn:&lt;name below the module&gt;</c> or the DbSet name.</summary>
internal sealed record Violation(string File, string Module, IReadOnlySet<string> Members);

/// <summary>Source scanner behind <see cref="ModuleBoundaryTests"/>; kept separate so it can be unit-tested on snippets.</summary>
internal static class ModuleBoundaryScanner
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex UsingRegex = new(
        @"^[ \t]*(?:global[ \t]+)?using[ \t]+(?:static[ \t]+)?(?:\w+[ \t]*=[ \t]*)?(?:global::)?BirraPoint\.Api\.Modules\.(?<module>\w+)(?<rest>(?:\.\w+)*)[ \t]*;",
        RegexOptions.Multiline | RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex QualifiedNameRegex = new(
        @"(?<![\w.])(?:global::)?BirraPoint\.Api\.Modules\.(?<module>\w+)(?<rest>(?:\.\w+)*)",
        RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex LineComment = new(@"//.*$", RegexOptions.Multiline | RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex NamespaceDeclaration = new(
        @"^[ \t]*namespace[ \t]+[\w.]+", RegexOptions.Multiline | RegexOptions.Compiled, RegexTimeout);

    public static string StripComments(string source) =>
        LineComment.Replace(BlockComment.Replace(source, string.Empty), string.Empty);

    private static bool IsContracts(string rest) =>
        rest == ".Contracts" || rest.StartsWith(".Contracts.", StringComparison.Ordinal);

    /// <summary>Violations in one file. <paramref name="ownModule"/> is null for kernel code (any module reference is
    /// foreign); <paramref name="dbSets"/> maps DbSet property name to owning module.</summary>
    public static IEnumerable<Violation> Scan(string relative, string source, string? ownModule, IReadOnlyDictionary<string, string> dbSets)
    {
        source = StripComments(source);

        foreach (Match match in UsingRegex.Matches(source))
        {
            var module = match.Groups["module"].Value;
            var rest = match.Groups["rest"].Value;
            if (module != ownModule && !IsContracts(rest))
            {
                yield return new Violation(relative, module, new HashSet<string> { $"using:{rest.TrimStart('.')}" });
            }
        }

        var code = NamespaceDeclaration.Replace(UsingRegex.Replace(source, string.Empty), string.Empty);
        foreach (Match match in QualifiedNameRegex.Matches(code))
        {
            var module = match.Groups["module"].Value;
            var rest = match.Groups["rest"].Value;
            if (module != ownModule && !IsContracts(rest))
            {
                yield return new Violation(relative, module, new HashSet<string> { $"fqn:{rest.TrimStart('.')}" });
            }
        }

        foreach (var (name, module) in dbSets)
        {
            if (module != ownModule
                && Regex.IsMatch(source, $@"\b\w*(?:[dD]bContext|[dD]b|[cC]ontext|[cC]tx)\s*\.\s*{name}\b", RegexOptions.None, RegexTimeout))
            {
                yield return new Violation(relative, module, new HashSet<string> { name });
            }
        }
    }
}
