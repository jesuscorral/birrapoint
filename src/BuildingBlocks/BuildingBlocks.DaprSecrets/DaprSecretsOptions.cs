namespace BuildingBlocks.DaprSecrets;

/// <summary>One secret to read from the store, by its name in the store (e.g. <c>ConnectionStrings--db</c>).</summary>
/// <param name="Name">Secret name; <see cref="DaprSecretsOptions.KeyDelimiter"/> stands for the <c>:</c> section separator.</param>
/// <param name="IsRequired">When <see langword="false"/>, a missing secret is skipped instead of failing startup.</param>
public sealed record DaprSecret(string Name, bool IsRequired = true);

/// <summary>What <see cref="DaprSecretsConfigurationExtensions.AddDaprSecrets"/> reads and how patiently.</summary>
public sealed class DaprSecretsOptions
{
    /// <summary>Configuration key naming the Dapr secret store component; the loader is off while it is blank.</summary>
    public string StoreKey { get; set; } = "Dapr:SecretStore";

    /// <summary>The secrets to load. Explicit on purpose: a shared store may hold secrets of other apps.</summary>
    public IList<DaprSecret> Secrets { get; } = [];

    /// <summary>Stands for <c>:</c> in secret names, for stores that allow only alphanumerics and <c>-</c> (Azure Key Vault).</summary>
    public string KeyDelimiter { get; set; } = "--";

    /// <summary>How long to wait for the sidecar, which may start after the application.</summary>
    public TimeSpan SidecarWaitTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Reads before giving up while the store refuses them (e.g. 403 until a new identity's role propagates).</summary>
    public int LoadAttempts { get; set; } = 15;

    /// <summary>Pause between refused reads.</summary>
    public TimeSpan LoadRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The first configuration problem, or <see langword="null"/> when the options are usable.</summary>
    internal string? FindProblem()
    {
        if (string.IsNullOrWhiteSpace(StoreKey))
        {
            return "DaprSecretsOptions.StoreKey must name a configuration key.";
        }

        if (Secrets.Count == 0)
        {
            return "DaprSecretsOptions.Secrets must list at least one secret to load.";
        }

        if (Secrets.Any(secret => string.IsNullOrWhiteSpace(secret.Name)))
        {
            return "Every secret in DaprSecretsOptions.Secrets needs a name.";
        }

        if (string.IsNullOrEmpty(KeyDelimiter))
        {
            return "DaprSecretsOptions.KeyDelimiter must not be empty.";
        }

        if (LoadAttempts < 1)
        {
            return "DaprSecretsOptions.LoadAttempts must be at least 1.";
        }

        return SidecarWaitTimeout < TimeSpan.Zero || LoadRetryDelay < TimeSpan.Zero
            ? "DaprSecretsOptions timeouts and delays must not be negative."
            : null;
    }
}
