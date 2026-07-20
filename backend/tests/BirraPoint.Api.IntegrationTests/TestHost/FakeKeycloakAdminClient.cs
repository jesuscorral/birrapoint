using BirraPoint.Api.Common.Keycloak;

namespace BirraPoint.Api.IntegrationTests.TestHost;

/// <summary>T039 test double — returns a canned temporary password instantly instead of calling a real Keycloak Admin REST API.</summary>
public sealed class FakeKeycloakAdminClient : IKeycloakAdminClient
{
    public Task<string> EnsureUserWithTemporaryPasswordAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult("Fake-Temp-Password-1!");

    public Task UpdateUserEmailAsync(string oldEmail, string newEmail, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
