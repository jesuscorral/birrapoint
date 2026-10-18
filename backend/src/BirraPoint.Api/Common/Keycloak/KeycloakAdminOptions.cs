using Microsoft.Extensions.Options;

namespace BirraPoint.Api.Common.Keycloak;

/// <summary>Bound from the <c>Keycloak</c> configuration section (T130). Only the admin client
/// secret is validated here; <see cref="KeycloakAdminClient"/> still reads the other keys directly.</summary>
internal sealed class KeycloakAdminOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Local-dev secret injected by the AppHost and the dev realm import; never valid outside dev.</summary>
    public const string LocalDevPlaceholder = "dev-only-secret-change-me";

    public string? AdminClientSecret { get; set; }
}

/// <summary>Fails startup outside Development/Testing when <c>Keycloak:AdminClientSecret</c> is
/// missing, blank or the local-dev placeholder. The message names the setting, never the value.</summary>
internal sealed class KeycloakAdminOptionsValidator(IHostEnvironment environment) : IValidateOptions<KeycloakAdminOptions>
{
    public ValidateOptionsResult Validate(string? name, KeycloakAdminOptions options)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return ValidateOptionsResult.Success;
        }

        if (string.IsNullOrWhiteSpace(options.AdminClientSecret))
        {
            return ValidateOptionsResult.Fail(
                "Keycloak:AdminClientSecret is not configured (Key Vault / Secrets Manager name Keycloak--AdminClientSecret).");
        }

        if (string.Equals(options.AdminClientSecret, KeycloakAdminOptions.LocalDevPlaceholder, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                "Keycloak:AdminClientSecret is still the local-development placeholder; set a real secret.");
        }

        return ValidateOptionsResult.Success;
    }
}
