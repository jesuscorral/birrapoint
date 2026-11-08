using BuildingBlocks.DaprSecrets;

namespace BirraPoint.Api.Extensions;

/// <summary>
/// ADR-0021/ADR-0023: in the cloud the API's secrets live in Key Vault (Azure) or Secrets Manager
/// (AWS) and are read through the Dapr secret store at startup (BuildingBlocks.DaprSecrets, ADR-0037)
/// instead of arriving as environment settings; they land in <see cref="IConfiguration"/> under
/// their usual keys, so no consumer changes. Enabled only when <c>Dapr:SecretStore</c> names the
/// component; locally the Aspire AppHost keeps injecting plain settings and no sidecar is needed.
/// </summary>
internal static class BirraPointSecrets
{
    public static void Configure(DaprSecretsOptions options)
    {
        options.StoreKey = "Dapr:SecretStore";

        // Explicit list (never the whole store): the vault also holds Keycloak's secrets, which the
        // API must not load. Names must match infra/{azure,aws}/terraform/secrets.tf.
        options.Secrets.Add(new DaprSecret("ConnectionStrings--db"));
        options.Secrets.Add(new DaprSecret("ConnectionStrings--dbDirect"));
        options.Secrets.Add(new DaprSecret("Keycloak--AdminClientSecret"));
        // Stored only when an SMTP password is configured (a relay without authentication has none).
        options.Secrets.Add(new DaprSecret("Smtp--Password", IsRequired: false));
    }
}
