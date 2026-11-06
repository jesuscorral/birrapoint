using Microsoft.Extensions.Configuration;

namespace BuildingBlocks.DaprSecrets.Tests;

public sealed class DaprSecretsConfigurationExtensionsTests
{
    private static int SourceCount(ConfigurationManager configuration) => ((IConfigurationBuilder)configuration).Sources.Count;

    [Fact]
    public void WithoutAStoreNoConfigurationSourceIsAdded()
    {
        using var configuration = new ConfigurationManager();
        var sourcesBefore = SourceCount(configuration);

        configuration.AddDaprSecrets(options => options.Secrets.Add(new DaprSecret("ConnectionStrings--db")));

        Assert.Equal(sourcesBefore, SourceCount(configuration));
    }

    [Fact]
    public void TheStoreKeyDefaultsToDaprSecretStore()
    {
        Assert.Equal("Dapr:SecretStore", new DaprSecretsOptions().StoreKey);
    }

    [Fact]
    public void AnEmptySecretListIsRejectedEvenWithoutAStore()
    {
        // A misconfigured host fails on every machine, not only once deployed next to a sidecar.
        using var configuration = new ConfigurationManager();

        var error = Assert.Throws<ArgumentException>(() => configuration.AddDaprSecrets(_ => { }));

        Assert.Contains("secret", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankSecretNameIsRejected(string name)
    {
        using var configuration = new ConfigurationManager();

        Assert.Throws<ArgumentException>(() => configuration.AddDaprSecrets(options => options.Secrets.Add(new DaprSecret(name))));
    }

    [Fact]
    public void ABlankStoreKeyIsRejected()
    {
        using var configuration = new ConfigurationManager();

        Assert.Throws<ArgumentException>(() => configuration.AddDaprSecrets(options =>
        {
            options.StoreKey = " ";
            options.Secrets.Add(new DaprSecret("ConnectionStrings--db"));
        }));
    }

    [Fact]
    public void FewerThanOneLoadAttemptIsRejected()
    {
        using var configuration = new ConfigurationManager();

        Assert.Throws<ArgumentException>(() => configuration.AddDaprSecrets(options =>
        {
            options.LoadAttempts = 0;
            options.Secrets.Add(new DaprSecret("ConnectionStrings--db"));
        }));
    }
}
