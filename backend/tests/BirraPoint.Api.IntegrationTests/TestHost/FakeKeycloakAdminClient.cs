using System.Collections.Concurrent;
using BirraPoint.Api.Common.Keycloak;

namespace BirraPoint.Api.IntegrationTests.TestHost;

/// <summary>T039 test double — returns a canned temporary password instantly instead of calling a real Keycloak Admin REST API.</summary>
public sealed class FakeKeycloakAdminClient : IKeycloakAdminClient
{
    private readonly ConcurrentDictionary<string, int> _passwordResetCallCounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registered singleton — used by regression tests to assert a judge's password was (not) reset again.</summary>
    public int PasswordResetCallCount(string email) => _passwordResetCallCounts.GetValueOrDefault(email);

    public Task<string> EnsureUserWithTemporaryPasswordAsync(string email, CancellationToken cancellationToken)
    {
        _passwordResetCallCounts.AddOrUpdate(email, 1, (_, count) => count + 1);
        return Task.FromResult("Fake-Temp-Password-1!");
    }

    public Task UpdateUserEmailAsync(string oldEmail, string newEmail, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
