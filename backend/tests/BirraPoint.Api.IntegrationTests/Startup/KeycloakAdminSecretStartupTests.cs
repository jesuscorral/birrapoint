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
    public void Production_host_without_admin_client_secret_fails_to_start()
    {
        var ex = StartProduction(secret: "");

        Assert.Contains("Keycloak:AdminClientSecret", ex.Message);
    }

    [Fact]
    public void Production_host_with_placeholder_admin_client_secret_fails_to_start_without_leaking_it()
    {
        var ex = StartProduction(secret: KeycloakAdminOptions.LocalDevPlaceholder);

        Assert.Contains("Keycloak:AdminClientSecret", ex.Message);
        Assert.DoesNotContain(KeycloakAdminOptions.LocalDevPlaceholder, ex.Message);
    }

    private static OptionsValidationException StartProduction(string secret)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
