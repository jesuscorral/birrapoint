using BirraPoint.Api.Common.Secrets;
using Dapr;
using Dapr.Client;
using Dapr.Extensions.Configuration.DaprSecretStore;
using Microsoft.Extensions.Configuration;

namespace BirraPoint.Api.UnitTests.Common.Secrets;

/// <summary>
/// T142 (ADR-0021): in Azure the API reads its secrets from the Dapr secret store backed by Key
/// Vault instead of from environment settings. The pure decisions — whether the store is enabled,
/// which secrets are requested and how their Key Vault names map onto configuration keys — are
/// kept out of Program.cs so they're unit-testable without a Dapr sidecar.
/// </summary>
public sealed class DaprSecretsTests
{
    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Store_is_disabled_when_Dapr_SecretStore_is_not_configured()
    {
        Assert.Null(DaprSecrets.ResolveStore(Build(new Dictionary<string, string?>())));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Store_is_disabled_when_Dapr_SecretStore_is_blank(string value)
    {
        var configuration = Build(new Dictionary<string, string?> { ["Dapr:SecretStore"] = value });

        Assert.Null(DaprSecrets.ResolveStore(configuration));
    }

    [Fact]
    public void Store_name_comes_from_Dapr_SecretStore()
    {
        var configuration = Build(new Dictionary<string, string?> { ["Dapr:SecretStore"] = " secretstore " });

        Assert.Equal("secretstore", DaprSecrets.ResolveStore(configuration));
    }

    [Fact]
    public void Without_a_store_no_configuration_source_is_added()
    {
        var configuration = new ConfigurationManager();
        var sourcesBefore = ((IConfigurationBuilder)configuration).Sources.Count;

        configuration.AddDaprSecrets();

        Assert.Equal(sourcesBefore, ((IConfigurationBuilder)configuration).Sources.Count);
    }

    [Fact]
    public void Source_requests_exactly_the_api_secrets_by_their_key_vault_names()
    {
        var source = new DaprSecretStoreConfigurationSource();
        using var client = new DaprClientBuilder().Build();

        DaprSecrets.Configure(source, "secretstore", client);

        Assert.Equal("secretstore", source.Store);
        Assert.Same(client, source.Client);
        Assert.Equal(
            ["ConnectionStrings--db", "ConnectionStrings--dbDirect", "Keycloak--AdminClientSecret", "Smtp--Password"],
            source.SecretDescriptors!.Select(d => d.SecretName));
    }

    [Fact]
    public void Only_the_smtp_password_is_optional()
    {
        var source = new DaprSecretStoreConfigurationSource();
        using var client = new DaprClientBuilder().Build();

        DaprSecrets.Configure(source, "secretstore", client);

        // Terraform stores Smtp--Password only when an SMTP password is configured (a relay
        // without authentication has none); every other secret must exist or startup fails.
        Assert.Equal(
            ["Smtp--Password"],
            source.SecretDescriptors!.Where(d => !d.IsRequired).Select(d => d.SecretName));
    }

    [Fact]
    public void Key_vault_double_dash_names_map_onto_configuration_sections()
    {
        var source = new DaprSecretStoreConfigurationSource();
        using var client = new DaprClientBuilder().Build();

        DaprSecrets.Configure(source, "secretstore", client);

        // Key Vault secret names allow only alphanumerics and '-', so "--" stands for ':'
        // (ConnectionStrings--db -> ConnectionStrings:db).
        Assert.True(source.NormalizeKey);
        Assert.Contains("--", source.KeyDelimiters!);
    }

    [Fact]
    public void Startup_waits_for_the_sidecar_instead_of_failing_immediately()
    {
        var source = new DaprSecretStoreConfigurationSource();
        using var client = new DaprClientBuilder().Build();

        DaprSecrets.Configure(source, "secretstore", client);

        Assert.False(source.IsOptional);
        Assert.True(source.SidecarWaitTimeout >= TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Loading_retries_while_the_store_refuses_and_returns_the_first_success()
    {
        // A brand-new app's identity only gets its Key Vault role after the app exists, so the
        // first reads can be refused (403) until the assignment propagates.
        var attempts = 0;
        var expected = new ConfigurationBuilder().Build();

        var result = DaprSecrets.LoadWithRetry(() =>
        {
            attempts++;
            return attempts < 3 ? throw new DaprException("403 ForbiddenByRbac") : expected;
        }, maxAttempts: 5, delay: TimeSpan.Zero);

        Assert.Same(expected, result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public void Loading_gives_up_after_the_last_attempt_with_the_store_error()
    {
        var attempts = 0;

        var error = Assert.Throws<DaprException>(() => DaprSecrets.LoadWithRetry(() =>
        {
            attempts++;
            throw new DaprException("403 ForbiddenByRbac");
        }, maxAttempts: 4, delay: TimeSpan.Zero));

        Assert.Equal(4, attempts);
        Assert.Contains("ForbiddenByRbac", error.Message);
    }

    [Fact]
    public void Loading_retries_a_store_error_wrapped_by_the_configuration_provider()
    {
        var attempts = 0;
        var expected = new ConfigurationBuilder().Build();

        var result = DaprSecrets.LoadWithRetry(() =>
        {
            attempts++;
            return attempts < 2
                ? throw new InvalidOperationException("load failed", new DaprException("403"))
                : expected;
        }, maxAttempts: 3, delay: TimeSpan.Zero);

        Assert.Same(expected, result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void Loading_does_not_retry_errors_unrelated_to_the_store()
    {
        var attempts = 0;

        Assert.Throws<ArgumentException>(() => DaprSecrets.LoadWithRetry(() =>
        {
            attempts++;
            throw new ArgumentException("bad configuration");
        }, maxAttempts: 5, delay: TimeSpan.Zero));

        Assert.Equal(1, attempts);
    }
}
