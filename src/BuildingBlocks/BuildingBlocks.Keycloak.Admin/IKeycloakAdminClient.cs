namespace BuildingBlocks.Keycloak.Admin;

/// <summary>User provisioning via the Keycloak Admin REST API: kept to the two calls hosts need, not a general SDK.</summary>
public interface IKeycloakAdminClient
{
    /// <summary>
    /// Idempotent on an existing account (one person's Keycloak user can be shared across
    /// personas): creates the user if missing, otherwise reuses it. A brand-new account, or an
    /// existing one that has never completed its first <c>UPDATE_PASSWORD</c>, gets a
    /// freshly-generated temporary password (<c>temporary: true</c>) and the plaintext password is
    /// returned, never persisted: the caller delivers it and discards it. An existing account that
    /// already completed setup keeps its own password untouched (only <paramref name="realmRole"/>
    /// is granted, since another persona under this email may be actively using that password) and
    /// this returns <see langword="null"/>.
    /// </summary>
    /// <param name="email">The user's email, also used as the username.</param>
    /// <param name="realmRole">The realm role granted to the user; it must exist in the realm.</param>
    /// <param name="cancellationToken">Cancels the Keycloak calls.</param>
    Task<string?> EnsureUserWithTemporaryPasswordAsync(string email, string realmRole, CancellationToken cancellationToken);

    /// <summary>No-op if no Keycloak account exists yet for <paramref name="oldEmail"/>.</summary>
    Task UpdateUserEmailAsync(string oldEmail, string newEmail, CancellationToken cancellationToken);
}
