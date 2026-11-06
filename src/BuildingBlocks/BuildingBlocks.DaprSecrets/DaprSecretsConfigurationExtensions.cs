using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.DaprSecrets;

/// <summary>Startup registration of the Dapr secret store as a configuration source.</summary>
public static class DaprSecretsConfigurationExtensions
{
    /// <summary>
    /// When <see cref="DaprSecretsOptions.StoreKey"/> names a secret store component, reads the
    /// listed secrets once, at startup, and adds them as the last (highest-precedence)
    /// configuration source under their usual keys (<c>ConnectionStrings--db</c> becomes
    /// <c>ConnectionStrings:db</c>), so no consumer changes. Waits for the sidecar and retries
    /// while the store refuses reads; fails startup when a required secret stays unreadable.
    /// Without the store key it adds nothing (local runs without a sidecar). The sidecar address
    /// comes from the <c>DAPR_HTTP_PORT</c>/<c>DAPR_GRPC_PORT</c> variables Dapr injects.
    /// </summary>
    /// <exception cref="ArgumentException">The options are invalid (checked even when the store is off).</exception>
    public static ConfigurationManager AddDaprSecrets(this ConfigurationManager configuration, Action<DaprSecretsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new DaprSecretsOptions();
        configure(options);
        if (options.FindProblem() is { } problem)
        {
            throw new ArgumentException(problem, nameof(configure));
        }

        var store = DaprSecretsLoader.ResolveStore(configuration, options.StoreKey);
        if (store is null)
        {
            return configuration;
        }

        configuration.AddConfiguration(DaprSecretsLoader.Load(store, options));
        return configuration;
    }
}
