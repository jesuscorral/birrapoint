namespace BirraPoint.Api.Common.Keycloak;

/// <summary>Judge provisioning via the Keycloak Admin REST API (R-10) — kept to the two calls this codebase needs, not a general SDK.</summary>
public interface IKeycloakAdminClient
{
    /// <summary>
    /// Idempotent on an existing account (one person's Keycloak user can be shared across
    /// competitions, per <see cref="Auth.JudgeResolver"/>): creates the user if missing, otherwise
    /// reuses it. A brand-new account, or an existing one that has never completed its first
    /// <c>UPDATE_PASSWORD</c>, gets a freshly-generated temporary password (<c>temporary: true</c>)
    /// and the plaintext password is returned — never persisted, the caller emails it and discards
    /// it. An existing account that already completed setup keeps its own password untouched
    /// (only the JUDGE role is granted, since some other persona under this email may be actively
    /// using that password) and this returns <see langword="null"/>.
    /// </summary>
    Task<string?> EnsureUserWithTemporaryPasswordAsync(string email, CancellationToken cancellationToken);

    /// <summary>No-op if no Keycloak account exists yet for <paramref name="oldEmail"/> (the judge's invitation hasn't been dispatched yet).</summary>
    Task UpdateUserEmailAsync(string oldEmail, string newEmail, CancellationToken cancellationToken);
}
