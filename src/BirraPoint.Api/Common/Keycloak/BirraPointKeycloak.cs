using BuildingBlocks.Keycloak.Admin;

namespace BirraPoint.Api.Common.Keycloak;

/// <summary>BirraPoint's choices for the reusable Keycloak admin block.</summary>
internal static class BirraPointKeycloak
{
    /// <summary>Realm role every provisioned judge account is granted; the realm's default roles never include it.</summary>
    public const string JudgeRole = "JUDGE";

    /// <summary>Local-dev secret injected by the AppHost and the dev realm import; never valid outside dev.</summary>
    public const string LocalDevPlaceholder = "dev-only-secret-change-me";

    public static void ConfigureAdmin(KeycloakAdminOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.DevelopmentPlaceholderSecret = LocalDevPlaceholder;
    }
}
