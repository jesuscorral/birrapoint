namespace BuildingBlocks.Keycloak.Authentication.Tests;

/// <summary>Locates a block's csproj by walking up from the test output folder to the folder holding <c>src/BuildingBlocks</c>.</summary>
internal static class BlockProjectFile
{
    public static string Find(string blockName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "BuildingBlocks", blockName, $"{blockName}.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{blockName}.csproj not found above {AppContext.BaseDirectory}.");
    }
}
