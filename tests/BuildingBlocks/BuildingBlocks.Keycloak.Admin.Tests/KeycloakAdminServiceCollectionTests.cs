using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Keycloak.Admin.Tests;

public sealed class KeycloakAdminServiceCollectionTests
{
    private const string Placeholder = "host-supplied-placeholder";

    private static ServiceProvider Build(
        string environment, Dictionary<string, string?> values, Action<KeycloakAdminOptions>? configure = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new Env(environment));
        services.AddKeycloakAdmin(configuration.GetSection("Keycloak"), configure);
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> Complete(string secret) => new()
    {
        ["Keycloak:Authority"] = "https://idp.test/realms/r",
        ["Keycloak:AdminClientId"] = "admin-client",
        ["Keycloak:AdminClientSecret"] = secret,
    };

    [Fact]
    public void BindsTheSectionAndAppliesTheHostConfiguration()
    {
        using var provider = Build("Production", Complete("real-secret"), options => options.DevelopmentPlaceholderSecret = Placeholder);

        var options = provider.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value;

        Assert.Equal("https://idp.test/realms/r", options.Authority);
        Assert.Equal("admin-client", options.AdminClientId);
        Assert.Equal("real-secret", options.AdminClientSecret);
        Assert.Equal(Placeholder, options.DevelopmentPlaceholderSecret);
    }

    [Fact]
    public void RegistersTheTypedClientAgainstTheInterface()
    {
        using var provider = Build("Production", Complete("real-secret"));

        Assert.IsType<KeycloakAdminClient>(provider.GetRequiredService<IKeycloakAdminClient>());
    }

    [Fact]
    public void RejectsAPlaceholderSecretOutsideDevelopmentWithoutEchoingIt()
    {
        using var provider = Build("Production", Complete(Placeholder), options => options.DevelopmentPlaceholderSecret = Placeholder);

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<KeycloakAdminOptions>>().Value);

        Assert.Contains("Keycloak:AdminClientSecret", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Placeholder, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatesOnHostStart()
    {
        using var provider = Build("Production", Complete(Placeholder), options => options.DevelopmentPlaceholderSecret = Placeholder);

        var startupValidator = provider.GetRequiredService<IStartupValidator>();

        Assert.Throws<OptionsValidationException>(startupValidator.Validate);
    }

    private sealed class Env(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Block.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
