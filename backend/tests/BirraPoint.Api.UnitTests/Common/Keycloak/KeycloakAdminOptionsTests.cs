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
    public void Outside_development_missing_blank_or_placeholder_secret_fails_naming_the_setting(string env, string? secret)
    {
        var result = ValidatorFor(env).Validate(null, new KeycloakAdminOptions { AdminClientSecret = secret });

        Assert.True(result.Failed);
        Assert.Contains("Keycloak:AdminClientSecret", result.FailureMessage);
        if (!string.IsNullOrWhiteSpace(secret))
        {
            // Never echo the value, even the well-known placeholder.
            Assert.DoesNotContain(secret, result.FailureMessage);
        }
    }

    [Fact]
    public void Outside_development_a_real_secret_is_accepted()
    {
        var result = ValidatorFor("Production").Validate(null, new KeycloakAdminOptions { AdminClientSecret = "s3cr3t-from-key-vault" });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Development", null)]
    [InlineData("Development", "dev-only-secret-change-me")]
    [InlineData("Testing", null)]
    [InlineData("Testing", "dev-only-secret-change-me")]
    public void Development_and_testing_environments_are_not_checked(string env, string? secret)
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
