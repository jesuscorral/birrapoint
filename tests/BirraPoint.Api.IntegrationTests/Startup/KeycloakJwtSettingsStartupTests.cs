using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BirraPoint.Api.IntegrationTests.Startup;

/// <summary>
/// Proves Program wires the Keycloak JWT bearer registration so a blank Keycloak:Authority or
/// Keycloak:ApiAudience stops the host at startup in every environment, naming the setting
/// (ADR-0037). No database is needed: registration fails before any request or migration.
/// </summary>
public sealed class KeycloakJwtSettingsStartupTests
{
    [Theory]
    [InlineData("Production", "Keycloak:ApiAudience", "Audience")]
    [InlineData("Development", "Keycloak:ApiAudience", "Audience")]
    [InlineData("Production", "Keycloak:Authority", "Authority")]
    [InlineData("Development", "Keycloak:Authority", "Authority")]
    public void HostWithBlankSettingFailsToStart(string environment, string settingKey, string optionName)
    {
        using var baseFactory = new WebApplicationFactory<Program>();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:db", "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1");
            builder.UseSetting("Frontend:BaseUrl", "http://localhost:4200");
            builder.UseSetting(settingKey, "");
        });

        var ex = Assert.Throws<InvalidOperationException>(() => _ = factory.Server);

        Assert.Contains(optionName, ex.Message, StringComparison.Ordinal);
    }
}
