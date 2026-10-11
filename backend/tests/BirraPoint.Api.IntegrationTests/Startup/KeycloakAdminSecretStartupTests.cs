using BirraPoint.Api.Common.Keycloak;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace BirraPoint.Api.IntegrationTests.Startup;

/// <summary>
/// T130: proves the KeycloakAdminOptions ValidateOnStart registration in Program.cs is wired — a
/// Production host without a real Keycloak:AdminClientSecret must refuse to start, naming the key
/// but never echoing the placeholder value. No database is needed: validation fails during host
/// start, before any request or migration.
/// </summary>
public sealed class KeycloakAdminSecretStartupTests
{
    [Fact]
    public void ProductionHostWithoutAdminClientSecretFailsToStart()
    {
        var ex = StartProduction(secret: "");

        Assert.Contains("Keycloak:AdminClientSecret", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionHostWithPlaceholderAdminClientSecretFailsToStartWithoutLeakingIt()
    {
        var ex = StartProduction(secret: KeycloakAdminOptions.LocalDevPlaceholder);

        Assert.Contains("Keycloak:AdminClientSecret", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(KeycloakAdminOptions.LocalDevPlaceholder, ex.Message, StringComparison.Ordinal);
    }

    private static OptionsValidationException StartProduction(string secret)
    {
        using var baseFactory = new WebApplicationFactory<Program>();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:db"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1",
                    ["Frontend:BaseUrl"] = "http://localhost:4200",
                    ["Keycloak:AdminClientSecret"] = secret,
                }));
        });

        return Assert.Throws<OptionsValidationException>(() => _ = factory.Server);
    }
}
