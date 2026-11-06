using BirraPoint.Api.Common.Secrets;
using BuildingBlocks.DaprSecrets;

namespace BirraPoint.Api.UnitTests.Common.Secrets;

/// <summary>
/// ADR-0021/ADR-0023: in the cloud the API reads its secrets from the Dapr secret store (Key Vault
/// or Secrets Manager). The loading mechanics live in BuildingBlocks.DaprSecrets; what stays here
/// is which secrets the API asks for, which must match the names Terraform stores
/// (infra/azure/terraform/secrets.tf, infra/aws/terraform/secrets.tf).
/// </summary>
public sealed class BirraPointSecretsTests
{
    private static DaprSecretsOptions Configured()
    {
        var options = new DaprSecretsOptions();
        BirraPointSecrets.Configure(options);
        return options;
    }

    [Fact]
    public void TheStoreIsNamedByDaprSecretStore()
    {
        Assert.Equal("Dapr:SecretStore", Configured().StoreKey);
    }

    [Fact]
    public void RequestsExactlyTheApiSecretsByTheirStoreNames()
    {
        Assert.Equal(
            ["ConnectionStrings--db", "ConnectionStrings--dbDirect", "Keycloak--AdminClientSecret", "Smtp--Password"],
            Configured().Secrets.Select(secret => secret.Name));
    }

    [Fact]
    public void OnlyTheSmtpPasswordIsOptional()
    {
        // Terraform stores Smtp--Password only when an SMTP password is configured (a relay
        // without authentication has none); every other secret must exist or startup fails.
        Assert.Equal(["Smtp--Password"], Configured().Secrets.Where(secret => !secret.IsRequired).Select(secret => secret.Name));
    }

    [Fact]
    public void StoreNamesUseTheKeyVaultSectionDelimiter()
    {
        Assert.Equal("--", Configured().KeyDelimiter);
    }
}
