namespace BirraPoint.Api.UnitTests.Architecture;

/// <summary>
/// The modules already extracted into their own project and the aggregates whose state is protected (FR-102, FR-104,
/// FR-105, FR-109). Both lists start empty: each module's extraction adds its name here in the same change that moves it,
/// and the final change replaces the list with "every module".
/// </summary>
internal static class ArchitectureScope
{
    /// <summary>Module names (<c>Catalog</c>, <c>Judging</c>, …); the project is <c>BirraPoint.&lt;name&gt;</c> under <c>src/Modules</c>.</summary>
    public static readonly string[] MigratedModules = [];

    /// <summary>Full type names of aggregates that must expose no public setter on their state.</summary>
    public static readonly string[] ProtectedAggregates = [];

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string ModuleFolder(string module) => Path.Combine(RepositoryRoot, "src", "Modules", $"BirraPoint.{module}");

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BirraPoint.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
