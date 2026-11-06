using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Keycloak.Admin.Tests;

public sealed class KeycloakAdminOptionsTests
{
    private const string Placeholder = "host-supplied-placeholder";

    private static KeycloakAdminOptionsValidator ValidatorFor(string environment) =>
        new(new FakeHostEnvironment(environment), "Keycloak");

    private static KeycloakAdminOptions Options(string? secret) => new()
    {
        Authority = "https://idp.test/realms/r",
        AdminClientId = "admin-client",
        AdminClientSecret = secret,
        DevelopmentPlaceholderSecret = Placeholder,
    };

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Production", "")]
    [InlineData("Production", "   ")]
    [InlineData("Staging", Placeholder)]
    [InlineData("Production", Placeholder)]
    public void OutsideDevelopmentMissingBlankOrPlaceholderSecretFailsNamingTheSetting(string env, string? secret)
    {
        var result = ValidatorFor(env).Validate(null, Options(secret));

        Assert.True(result.Failed);
        Assert.Contains("Keycloak:AdminClientSecret", result.FailureMessage, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(secret))
        {
            // Never echo the value, even the well-known placeholder.
            Assert.DoesNotContain(secret, result.FailureMessage, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WithoutAHostPlaceholderOnlyMissingOrBlankSecretsFail()
    {
        var options = Options(Placeholder);
        options.DevelopmentPlaceholderSecret = null;

        Assert.True(ValidatorFor("Production").Validate(null, options).Succeeded);
    }

    [Fact]
    public void MessagesUseTheConfiguredSectionPath()
    {
        var validator = new KeycloakAdminOptionsValidator(new FakeHostEnvironment("Production"), "Identity:Admin");

        var result = validator.Validate(null, Options(null));

        Assert.Contains("Identity:Admin:AdminClientSecret", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void OutsideDevelopmentARealSecretIsAccepted()
    {
        var result = ValidatorFor("Production").Validate(null, Options("s3cr3t-from-key-vault"));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null, "admin-client", "Authority")]
    [InlineData("  ", "admin-client", "Authority")]
    [InlineData("https://idp.test/realms/r", null, "AdminClientId")]
    [InlineData("https://idp.test/realms/r", " ", "AdminClientId")]
    public void OutsideDevelopmentMissingAuthorityOrClientIdFailsNamingTheSetting(string? authority, string? clientId, string setting)
    {
        var options = Options("s3cr3t-from-key-vault");
        options.Authority = authority;
        options.AdminClientId = clientId;

        var result = ValidatorFor("Production").Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains($"Keycloak:{setting}", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development", null)]
    [InlineData("Development", Placeholder)]
    [InlineData("Testing", null)]
    [InlineData("Testing", Placeholder)]
    public void DevelopmentAndTestingEnvironmentsAreNotChecked(string env, string? secret)
    {
        var options = Options(secret);
        options.Authority = null;

        var result = ValidatorFor(env).Validate(null, options);

        Assert.True(result.Succeeded);
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Block.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
