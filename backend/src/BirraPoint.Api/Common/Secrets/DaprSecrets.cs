using Dapr.Client;
using Dapr.Extensions.Configuration;
using Dapr.Extensions.Configuration.DaprSecretStore;

namespace BirraPoint.Api.Common.Secrets;

/// <summary>
/// T142 (ADR-0021): in Azure the API's secrets live in Key Vault and are read through the Dapr
/// secret store component (<c>secretstores.azure.keyvault</c>, authenticated with the Container
/// App's system-assigned managed identity) instead of arriving as environment settings. The
/// secrets are loaded once at startup into <see cref="IConfiguration"/> under their usual keys
/// (<c>ConnectionStrings:db</c>, <c>Keycloak:AdminClientSecret</c>...), so no consumer changes.
/// Enabled only when <c>Dapr:SecretStore</c> names the component; locally the Aspire AppHost keeps
/// injecting plain settings and no sidecar is needed.
/// </summary>
public static class DaprSecrets
{
    public const string SecretStoreKey = "Dapr:SecretStore";

    // Key Vault secret names allow only alphanumerics and '-': "--" stands for the ':' section
    // separator (ConnectionStrings--db -> ConnectionStrings:db).
    private const string KeyVaultKeyDelimiter = "--";

    // Explicit list (never the whole store): the vault also holds Keycloak's secrets, which the
    // API must not load.
    private static readonly DaprSecretDescriptor[] Descriptors =
    [
        new("ConnectionStrings--db"),
        new("ConnectionStrings--dbDirect"),
        new("Keycloak--AdminClientSecret"),
        // Stored only when an SMTP password is configured (a relay without authentication has none).
        new("Smtp--Password", false),
    ];

    // The sidecar starts alongside the app container; give it time before failing startup.
    private static readonly TimeSpan SidecarWaitTimeout = TimeSpan.FromSeconds(60);

    public static string? ResolveStore(IConfiguration configuration)
    {
        var store = configuration[SecretStoreKey];
        return string.IsNullOrWhiteSpace(store) ? null : store.Trim();
    }

    public static void Configure(DaprSecretStoreConfigurationSource source, string store, DaprClient client)
    {
        source.Store = store;
        source.Client = client;
        source.SecretDescriptors = Descriptors;
        source.NormalizeKey = true;
        source.KeyDelimiters = [KeyVaultKeyDelimiter];
        source.SidecarWaitTimeout = SidecarWaitTimeout;
        source.IsOptional = false;
    }

    /// <summary>
    /// Adds the Dapr secret store as the last (highest-precedence) configuration source when
    /// <c>Dapr:SecretStore</c> is set. The sidecar address comes from the DAPR_HTTP_PORT /
    /// DAPR_GRPC_PORT variables Container Apps injects.
    /// </summary>
    public static ConfigurationManager AddDaprSecrets(this ConfigurationManager configuration)
    {
        var store = ResolveStore(configuration);
        if (store is null)
        {
            return configuration;
        }

        // Lives for the application's lifetime: the configuration provider keeps using it.
        var client = new DaprClientBuilder().Build();
        configuration.AddDaprSecretStore(source => Configure(source, store, client));
        return configuration;
    }
}
