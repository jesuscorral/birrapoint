using System.Reflection;
using System.Text.Json;

namespace BirraPoint.Api.Features.Catalog.Data;

/// <summary>Reads the embedded bjcp-2021.json seed resource (T010, R-12).</summary>
public static class BjcpStyleCatalogLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<BjcpStyleSeedRecord> Load()
    {
        var assembly = typeof(BjcpStyleCatalogLoader).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith("bjcp-2021.json", StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "Embedded resource 'bjcp-2021.json' not found in the BirraPoint.Api assembly.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        return JsonSerializer.Deserialize<List<BjcpStyleSeedRecord>>(stream, JsonOptions)
            ?? throw new InvalidOperationException("bjcp-2021.json deserialized to null.");
    }
}
