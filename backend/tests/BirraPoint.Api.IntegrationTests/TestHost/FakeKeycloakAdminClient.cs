using System.Collections.Concurrent;
using BirraPoint.Api.Common.Keycloak;

namespace BirraPoint.Api.IntegrationTests.TestHost;

/// <summary>T039 test double — returns a canned temporary password instantly instead of calling a real Keycloak Admin REST API.</summary>
public sealed class FakeKeycloakAdminClient : IKeycloakAdminClient
{
    private readonly ConcurrentDictionary<string, int> _passwordResetCallCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _activeAccounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registered singleton — used by regression tests to assert a judge's password was (not) reset again.</summary>
    public int PasswordResetCallCount(string email) => _passwordResetCallCounts.GetValueOrDefault(email);

    /// <summary>
    /// Test seam: marks <paramref name="email"/> as an already-active Keycloak account (its own
    /// first-login UPDATE_PASSWORD already completed), so a subsequent
    /// EnsureUserWithTemporaryPasswordAsync call skips the reset — mirrors the real
    /// KeycloakAdminClient's behavior for an account shared across competitions/roles.
    /// </summary>
    public void MarkAccountActive(string email) => _activeAccounts[email] = 0;

    public Task<string?> EnsureUserWithTemporaryPasswordAsync(string email, CancellationToken cancellationToken)
    {
        if (_activeAccounts.ContainsKey(email))
        {
            return Task.FromResult<string?>(null);
        }

        _passwordResetCallCounts.AddOrUpdate(email, 1, (_, count) => count + 1);
        return Task.FromResult<string?>("Fake-Temp-Password-1!");
    }

    public Task UpdateUserEmailAsync(string oldEmail, string newEmail, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
