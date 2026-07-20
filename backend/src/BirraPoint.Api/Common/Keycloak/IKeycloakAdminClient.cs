namespace BirraPoint.Api.Common.Keycloak;

/// <summary>Judge provisioning via the Keycloak Admin REST API (R-10) — kept to the two calls this codebase needs, not a general SDK.</summary>
public interface IKeycloakAdminClient
{
    /// <summary>
    /// Idempotent on an existing account (one person's Keycloak user can be shared across
    /// competitions, per <see cref="Auth.JudgeResolver"/>): creates the user if missing, otherwise
    /// reuses it. Either way, sets a freshly-generated temporary password (<c>temporary: true</c>)
    /// and ensures the <c>UPDATE_PASSWORD</c> required action is present, then returns the
    /// plaintext password — never persisted, the caller emails it and discards it.
    /// </summary>
    Task<string> EnsureUserWithTemporaryPasswordAsync(string email, CancellationToken cancellationToken);

    /// <summary>No-op if no Keycloak account exists yet for <paramref name="oldEmail"/> (the judge's invitation hasn't been dispatched yet).</summary>
    Task UpdateUserEmailAsync(string oldEmail, string newEmail, CancellationToken cancellationToken);
}
