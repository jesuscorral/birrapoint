using System.Runtime.CompilerServices;

namespace BirraPoint.Api.IntegrationTests.TestHost;

/// <summary>
/// Programs read Keycloak settings while building the host, before a test factory's own
/// configuration callbacks apply, and the JWT block fails fast on a blank authority/audience. The
/// real Keycloak is never contacted: every factory swaps the validation parameters and the admin
/// client for test doubles.
/// </summary>
internal static class KeycloakTestSettings
{
#pragma warning disable CA2255 // The module initializer is the only hook that runs before any factory builds a host.
    [ModuleInitializer]
    internal static void Apply()
    {
        Environment.SetEnvironmentVariable("Keycloak__Authority", "https://keycloak.integration-tests.invalid/realms/birrapoint");
        Environment.SetEnvironmentVariable("Keycloak__ApiAudience", "birrapoint-api");
        Environment.SetEnvironmentVariable("Keycloak__AdminClientId", "birrapoint-api-admin");
    }
#pragma warning restore CA2255
}
