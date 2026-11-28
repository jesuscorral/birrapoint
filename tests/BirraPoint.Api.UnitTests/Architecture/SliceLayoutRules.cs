namespace BirraPoint.Api.UnitTests.Architecture;

/// <summary>
/// File-system rules of the flat slice layout (FR-104): a slice folder holds <c>Endpoint.cs</c>, <c>Request.cs</c>,
/// <c>Handler.cs</c> and optionally <c>Response.cs</c>; an area holds one <c>Setup.cs</c>; the old nesting and naming
/// conventions are gone.
/// </summary>
internal static class SliceLayoutRules
{
    private static readonly string[] RequiredSliceFiles = ["Endpoint.cs", "Request.cs", "Handler.cs"];

    private static readonly string[] AllowedSliceFiles = ["Endpoint.cs", "Request.cs", "Handler.cs", "Response.cs"];

    private static readonly string[] ForbiddenFolders = ["Features", "CommonComponents"];

    /// <summary>Violations under <paramref name="moduleFolder"/> (a module project folder), as paths relative to it.</summary>
    public static IReadOnlyList<string> Violations(string moduleFolder)
    {
        var violations = new List<string>();
        var directories = Directory.EnumerateDirectories(moduleFolder, "*", SearchOption.AllDirectories)
            .Where(d => !IsBuildOutput(moduleFolder, d))
            .Prepend(moduleFolder)
            .ToList();

        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);
            if (ForbiddenFolders.Contains(name, StringComparer.Ordinal))
            {
                violations.Add($"{Relative(moduleFolder, directory)}: folder name '{name}' is not allowed");
            }

            var files = Directory.GetFiles(directory, "*.cs").Select(Path.GetFileName).OfType<string>().ToList();

            if (files.Contains("Endpoint.cs", StringComparer.Ordinal))
            {
                violations.AddRange(RequiredSliceFiles
                    .Where(required => !files.Contains(required, StringComparer.Ordinal))
                    .Select(required => $"{Relative(moduleFolder, directory)}: slice is missing {required}"));
                violations.AddRange(files
                    .Where(file => !AllowedSliceFiles.Contains(file, StringComparer.Ordinal))
                    .Select(file => $"{Relative(moduleFolder, directory)}: slice folder holds unexpected {file}"));
                if (files.Contains("Setup.cs", StringComparer.Ordinal))
                {
                    violations.Add($"{Relative(moduleFolder, directory)}: Setup.cs belongs to the area, not to a slice");
                }
            }

            violations.AddRange(files
                .Where(file => file.StartsWith("Base", StringComparison.Ordinal) && file.EndsWith("Endpoints.cs", StringComparison.Ordinal)
                    || file.EndsWith("ModuleEndpoints.cs", StringComparison.Ordinal))
                .Select(file => $"{Relative(moduleFolder, directory)}: {file} is a retired naming convention"));
        }

        // Every folder that directly contains slices (an area) has exactly one Setup.cs, at that level.
        foreach (var area in directories.Where(d => Directory.GetDirectories(d).Any(HasEndpoint)))
        {
            if (!File.Exists(Path.Combine(area, "Setup.cs")))
            {
                violations.Add($"{Relative(moduleFolder, area)}: area has slices but no Setup.cs");
            }
        }

        return violations.Order(StringComparer.Ordinal).ToList();
    }

    private static bool HasEndpoint(string directory) => File.Exists(Path.Combine(directory, "Endpoint.cs"));

    private static bool IsBuildOutput(string root, string directory)
    {
        var first = Relative(root, directory).Split('/')[0];
        return first is "bin" or "obj";
    }

    private static string Relative(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        return relative == "." ? "<module>" : relative;
    }
}
