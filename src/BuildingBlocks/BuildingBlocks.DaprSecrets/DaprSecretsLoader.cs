using Dapr;
using Dapr.Client;
using Dapr.Extensions.Configuration;
using Dapr.Extensions.Configuration.DaprSecretStore;
using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.DaprSecrets;

/// <summary>The decisions behind <see cref="DaprSecretsConfigurationExtensions.AddDaprSecrets"/>, kept pure so they are testable without a sidecar.</summary>
internal static class DaprSecretsLoader
{
    public static string? ResolveStore(IConfiguration configuration, string storeKey)
    {
        var store = configuration[storeKey];
        return string.IsNullOrWhiteSpace(store) ? null : store.Trim();
    }

    public static void Configure(
        DaprSecretStoreConfigurationSource source, string store, DaprClient client, DaprSecretsOptions options)
    {
        source.Store = store;
        source.Client = client;
        source.SecretDescriptors = [.. options.Secrets.Select(secret => new DaprSecretDescriptor(secret.Name, secret.IsRequired))];
        source.NormalizeKey = true;
        source.KeyDelimiters = [options.KeyDelimiter];
        source.SidecarWaitTimeout = options.SidecarWaitTimeout;
        source.IsOptional = false;
    }

    public static IConfigurationRoot Load(string store, DaprSecretsOptions options)
    {
        // Owned by the loaded configuration for the application's lifetime (never disposed: the
        // provider keeps the reference, and the process ends with it).
        var client = new DaprClientBuilder().Build();
        return LoadWithRetry(
            () => new ConfigurationBuilder().AddDaprSecretStore(source => Configure(source, store, client, options)).Build(),
            options.LoadAttempts,
            options.LoadRetryDelay);
    }

    /// <summary>
    /// Runs <paramref name="load"/> until it succeeds, retrying only errors raised by the Dapr
    /// store (<see cref="DaprException"/>, possibly wrapped by the configuration provider); the
    /// last error is rethrown after <paramref name="maxAttempts"/>. Messages name the secret, never
    /// its value.
    /// </summary>
    public static IConfigurationRoot LoadWithRetry(Func<IConfigurationRoot> load, int maxAttempts, TimeSpan delay)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                return load();
            }
            catch (Exception ex) when (attempt < maxAttempts && IsStoreError(ex))
            {
                // Runs before logging is configured, so stderr is the only channel.
                Console.Error.WriteLine(
                    $"Dapr secret store not readable yet (attempt {attempt}/{maxAttempts}): {ex.GetBaseException().Message}");
                Thread.Sleep(delay);
            }
        }
    }

    private static bool IsStoreError(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is DaprException)
            {
                return true;
            }
        }
        return false;
    }
}
