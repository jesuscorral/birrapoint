using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Keycloak.Admin;

/// <summary>Settings for the Admin REST client; bound from the configuration section given to <see cref="KeycloakAdminServiceCollectionExtensions.AddKeycloakAdmin"/>.</summary>
public sealed class KeycloakAdminOptions
{
    /// <summary>The realm's issuer URL (<c>https://host/realms/name</c>); the admin API base is derived from it.</summary>
    public string? Authority { get; set; }

    /// <summary>Confidential client with a service account allowed to manage users (client-credentials grant).</summary>
    public string? AdminClientId { get; set; }

    public string? AdminClientSecret { get; set; }

    /// <summary>A well-known development secret the host ships; rejected outside Development/Testing. Set it in code, not configuration.</summary>
    public string? DevelopmentPlaceholderSecret { get; set; }
}

/// <summary>
/// Fails startup outside Development/Testing when the authority, client id or client secret is
/// missing, or the secret is blank or the host's development placeholder. Messages name the
/// setting, never the value.
/// </summary>
internal sealed class KeycloakAdminOptionsValidator(IHostEnvironment environment, string sectionPath)
    : IValidateOptions<KeycloakAdminOptions>
{
    public ValidateOptionsResult Validate(string? name, KeycloakAdminOptions options)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return ValidateOptionsResult.Success;
        }

        List<string> failures = [];
        if (string.IsNullOrWhiteSpace(options.Authority))
        {
            failures.Add($"{sectionPath}:Authority is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.AdminClientId))
        {
            failures.Add($"{sectionPath}:AdminClientId is not configured.");
        }

        if (string.IsNullOrWhiteSpace(options.AdminClientSecret))
        {
            failures.Add($"{sectionPath}:AdminClientSecret is not configured.");
        }
        else if (options.DevelopmentPlaceholderSecret is not null &&
                 string.Equals(options.AdminClientSecret, options.DevelopmentPlaceholderSecret, StringComparison.Ordinal))
        {
            failures.Add($"{sectionPath}:AdminClientSecret is still the development placeholder; set a real secret.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
