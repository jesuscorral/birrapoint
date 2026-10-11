using BirraPoint.Api.Common.Keycloak;
using Microsoft.Extensions.Hosting;

namespace BirraPoint.Api.UnitTests.Common.Keycloak;

public sealed class KeycloakAdminOptionsTests
{
    private static KeycloakAdminOptionsValidator ValidatorFor(string environment) =>
        new(new FakeHostEnvironment(environment));

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Production", "")]
    [InlineData("Production", "   ")]
    [InlineData("Staging", "dev-only-secret-change-me")]
    [InlineData("Production", "dev-only-secret-change-me")]
    public void OutsideDevelopmentMissingBlankOrPlaceholderSecretFailsNamingTheSetting(string env, string? secret)
    {
        var result = ValidatorFor(env).Validate(null, new KeycloakAdminOptions { AdminClientSecret = secret });

        Assert.True(result.Failed);
        Assert.Contains("Keycloak:AdminClientSecret", result.FailureMessage, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(secret))
        {
            // Never echo the value, even the well-known placeholder.
            Assert.DoesNotContain(secret, result.FailureMessage, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OutsideDevelopmentARealSecretIsAccepted()
    {
        var result = ValidatorFor("Production").Validate(null, new KeycloakAdminOptions { AdminClientSecret = "s3cr3t-from-key-vault" });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Development", null)]
    [InlineData("Development", "dev-only-secret-change-me")]
    [InlineData("Testing", null)]
    [InlineData("Testing", "dev-only-secret-change-me")]
    public void DevelopmentAndTestingEnvironmentsAreNotChecked(string env, string? secret)
    {
        var result = ValidatorFor(env).Validate(null, new KeycloakAdminOptions { AdminClientSecret = secret });

        Assert.True(result.Succeeded);
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "BirraPoint.Api";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
