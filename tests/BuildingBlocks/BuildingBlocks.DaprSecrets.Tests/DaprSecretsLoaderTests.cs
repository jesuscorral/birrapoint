using Dapr;
using Dapr.Client;
using Dapr.Extensions.Configuration.DaprSecretStore;
using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.DaprSecrets.Tests;

/// <summary>
/// The pure decisions of the loader — whether the store is enabled, which secrets are requested,
/// how their names map onto configuration keys and when a failed read is retried — tested without
/// a Dapr sidecar.
/// </summary>
public sealed class DaprSecretsLoaderTests
{
    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static DaprSecretsOptions Options() => new()
    {
        Secrets = { new DaprSecret("ConnectionStrings--db"), new DaprSecret("Mail--Password", IsRequired: false) },
    };

    [Fact]
    public void StoreIsDisabledWhenTheStoreKeyIsNotConfigured()
    {
        Assert.Null(DaprSecretsLoader.ResolveStore(Build([]), "Dapr:SecretStore"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void StoreIsDisabledWhenTheStoreKeyIsBlank(string value)
    {
        var configuration = Build(new Dictionary<string, string?> { ["Dapr:SecretStore"] = value });

        Assert.Null(DaprSecretsLoader.ResolveStore(configuration, "Dapr:SecretStore"));
    }

    [Fact]
    public void StoreNameComesFromTheConfiguredKey()
    {
        var configuration = Build(new Dictionary<string, string?> { ["Secrets:Store"] = " vault " });

        Assert.Equal("vault", DaprSecretsLoader.ResolveStore(configuration, "Secrets:Store"));
    }

    [Fact]
    public void SourceRequestsExactlyTheHostSecretsWithTheirRequiredFlags()
    {
        var source = new DaprSecretStoreConfigurationSource();
        using var client = new DaprClientBuilder().Build();

        DaprSecretsLoader.Configure(source, "secretstore", client, Options());

        Assert.Equal("secretstore", source.Store);
        Assert.Same(client, source.Client);
        Assert.Equal(["ConnectionStrings--db", "Mail--Password"], source.SecretDescriptors!.Select(d => d.SecretName));
        Assert.Equal(["Mail--Password"], source.SecretDescriptors!.Where(d => !d.IsRequired).Select(d => d.SecretName));
    }

    [Fact]
    public void DoubleDashNamesMapOntoConfigurationSections()
    {
        var source = new DaprSecretStoreConfigurationSource();
        using var client = new DaprClientBuilder().Build();

        DaprSecretsLoader.Configure(source, "secretstore", client, Options());

        // Key Vault secret names allow only alphanumerics and '-', so "--" stands for ':'
        // (ConnectionStrings--db -> ConnectionStrings:db).
        Assert.True(source.NormalizeKey);
        Assert.Equal(["--"], source.KeyDelimiters);
    }

    [Fact]
    public void StartupWaitsForTheSidecarInsteadOfFailingImmediately()
    {
        var source = new DaprSecretStoreConfigurationSource();
        using var client = new DaprClientBuilder().Build();
        var options = Options();
        options.SidecarWaitTimeout = TimeSpan.FromSeconds(42);

        DaprSecretsLoader.Configure(source, "secretstore", client, options);

        Assert.False(source.IsOptional);
        Assert.Equal(TimeSpan.FromSeconds(42), source.SidecarWaitTimeout);
    }

    [Fact]
    public void LoadingRetriesWhileTheStoreRefusesAndReturnsTheFirstSuccess()
    {
        // A brand-new identity can be refused (403) until its role assignment propagates.
        var attempts = 0;
        var expected = new ConfigurationBuilder().Build();

        var result = DaprSecretsLoader.LoadWithRetry(() =>
        {
            attempts++;
            return attempts < 3 ? throw new DaprException("403 ForbiddenByRbac") : expected;
        }, maxAttempts: 5, delay: TimeSpan.Zero);

        Assert.Same(expected, result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public void LoadingGivesUpAfterTheLastAttemptWithTheStoreError()
    {
        var attempts = 0;

        var error = Assert.Throws<DaprException>(() => DaprSecretsLoader.LoadWithRetry(() =>
        {
            attempts++;
            throw new DaprException("403 ForbiddenByRbac");
        }, maxAttempts: 4, delay: TimeSpan.Zero));

        Assert.Equal(4, attempts);
        Assert.Contains("ForbiddenByRbac", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadingRetriesAStoreErrorWrappedByTheConfigurationProvider()
    {
        var attempts = 0;
        var expected = new ConfigurationBuilder().Build();

        var result = DaprSecretsLoader.LoadWithRetry(() =>
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
    public void LoadingDoesNotRetryErrorsUnrelatedToTheStore()
    {
        var attempts = 0;

        Assert.Throws<InvalidOperationException>(() => DaprSecretsLoader.LoadWithRetry(() =>
        {
            attempts++;
            throw new InvalidOperationException("bad configuration");
        }, maxAttempts: 5, delay: TimeSpan.Zero));

        Assert.Equal(1, attempts);
    }
}
