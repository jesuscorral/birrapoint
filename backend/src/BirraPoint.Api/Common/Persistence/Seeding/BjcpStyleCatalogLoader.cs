using System.Security.Cryptography;
using System.Text.Json;

namespace BirraPoint.Api.Common.Persistence.Seeding;

/// <summary>
/// Reads the embedded bjcp-2021.json seed resource (T010, R-12). Lives in the shared persistence
/// kernel — not under Features/Catalog/ — because the AddBjcpStyleCatalogDetails migration
/// depends on it, and Common/Persistence/ must never depend on a feature slice.
/// </summary>
public static class BjcpStyleCatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<BjcpStyleSeedRecord> Load()
    {
        using var stream = OpenResourceStream();
        return JsonSerializer.Deserialize<List<BjcpStyleSeedRecord>>(stream, JsonOptions)
            ?? throw new InvalidOperationException("bjcp-2021.json deserialized to null.");
    }

    /// <summary>
    /// SHA-256 of the raw embedded resource bytes. Pinned by a unit test so that any future edit
    /// to bjcp-2021.json fails fast instead of silently diverging: once AddBjcpStyleCatalogDetails
    /// has shipped, editing the JSON in place does NOT retroactively update already-migrated
    /// databases — a content change needs a new follow-up migration, not a silent file edit.
    /// </summary>
    public static string ComputeContentHash()
    {
        using var stream = OpenResourceStream();
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static Stream OpenResourceStream()
    {
        var assembly = typeof(BjcpStyleCatalogLoader).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith("bjcp-2021.json", StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "Embedded resource 'bjcp-2021.json' not found in the BirraPoint.Api assembly.");

        return assembly.GetManifestResourceStream(resourceName)!;
    }
}
