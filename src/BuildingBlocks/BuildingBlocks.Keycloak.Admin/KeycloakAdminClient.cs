using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Keycloak.Admin;

internal sealed class KeycloakAdminClient : IKeycloakAdminClient
{
    private readonly HttpClient _httpClient;
    private readonly string _authority;
    private readonly string _clientId;
    private readonly string _clientSecret;

    public KeycloakAdminClient(HttpClient httpClient, IOptions<KeycloakAdminOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = httpClient;
        var value = options.Value;
        _authority = value.Authority ?? throw new InvalidOperationException("Keycloak Authority is not configured.");
        _clientId = value.AdminClientId ?? throw new InvalidOperationException("Keycloak AdminClientId is not configured.");
        _clientSecret = value.AdminClientSecret ?? throw new InvalidOperationException("Keycloak AdminClientSecret is not configured.");
    }

    // ".../realms/birrapoint" -> ".../admin/realms/birrapoint" — no separate config key needed.
    private string AdminRealmBaseUrl => _authority.Replace("/realms/", "/admin/realms/", StringComparison.Ordinal);

    public async Task<string?> EnsureUserWithTemporaryPasswordAsync(string email, string realmRole, CancellationToken cancellationToken)
    {
        var token = await GetAdminAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var existingUser = await FindUserByEmailAsync(email, token, cancellationToken).ConfigureAwait(false);

        string userId;
        bool needsPassword;
        if (existingUser is null)
        {
            userId = await CreateUserAsync(email, token, cancellationToken).ConfigureAwait(false);
            needsPassword = true;
        }
        else
        {
            userId = existingUser["id"]!.GetValue<string>();
            needsPassword = HasPendingPasswordUpdate(existingUser);
        }

        await EnsureRealmRoleAsync(userId, realmRole, token, cancellationToken).ConfigureAwait(false);

        if (!needsPassword)
        {
            // Account already completed its own UPDATE_PASSWORD setup: another persona sharing this
            // email may be actively using this password, so granting the role must not reset it.
            return null;
        }

        var temporaryPassword = GenerateTemporaryPassword();
        await ResetPasswordAsync(userId, temporaryPassword, token, cancellationToken).ConfigureAwait(false);
        return temporaryPassword;
    }

    public async Task UpdateUserEmailAsync(string oldEmail, string newEmail, CancellationToken cancellationToken)
    {
        var token = await GetAdminAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var existingUser = await FindUserByEmailAsync(oldEmail, token, cancellationToken).ConfigureAwait(false);
        if (existingUser is null)
        {
            // Not provisioned yet — the caller creates the account under the corrected address
            // when it provisions the user.
            return;
        }

        var userId = existingUser["id"]!.GetValue<string>();
        existingUser["email"] = newEmail;
        existingUser["username"] = newEmail;

        await PutUserAsync(userId, existingUser, token, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetAdminAccessTokenAsync(CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
        };

        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(
            new Uri($"{_authority}/protocol/openid-connect/token", UriKind.Absolute), content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken).ConfigureAwait(false);
        return payload?["access_token"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Keycloak token response did not include an access_token.");
    }

    private async Task<JsonObject?> FindUserByEmailAsync(string email, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{AdminRealmBaseUrl}/users?email={Uri.EscapeDataString(email)}&exact=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var users = await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken).ConfigureAwait(false);
        return users?.OfType<JsonObject>().FirstOrDefault();
    }

    private async Task<string> CreateUserAsync(string email, string token, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["username"] = email,
            ["email"] = email,
            ["enabled"] = true,
            ["emailVerified"] = true,
            ["requiredActions"] = new JsonArray("UPDATE_PASSWORD"),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{AdminRealmBaseUrl}/users")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Keycloak's user-create endpoint returns 201 with no body; the new id is the last
        // segment of the Location header.
        var location = response.Headers.Location
            ?? throw new InvalidOperationException("Keycloak user creation response did not include a Location header.");
        return location.Segments[^1];
    }

    // The realm's default-roles composite (assigned automatically on user creation) is not expected
    // to grant the role, so every account, new or pre-existing, needs this explicit grant. Looked
    // up via the user-scoped "available roles" endpoint rather than the realm-scoped /roles/{name}:
    // the latter requires the realm-management view-realm permission, which a least-privilege
    // service account (manage-users/view-users only) does not have.
    private async Task EnsureRealmRoleAsync(string userId, string realmRole, string token, CancellationToken cancellationToken)
    {
        using var availableRolesRequest = new HttpRequestMessage(
            HttpMethod.Get, $"{AdminRealmBaseUrl}/users/{userId}/role-mappings/realm/available");
        availableRolesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var availableRolesResponse = await _httpClient.SendAsync(availableRolesRequest, cancellationToken).ConfigureAwait(false);
        availableRolesResponse.EnsureSuccessStatusCode();

        var availableRoles = await availableRolesResponse.Content.ReadFromJsonAsync<JsonArray>(cancellationToken).ConfigureAwait(false);
        var role = availableRoles?.OfType<JsonObject>().FirstOrDefault(candidate => string.Equals(candidate["name"]?.GetValue<string>(), realmRole, StringComparison.Ordinal));
        if (role is null)
        {
            // Absent from "available" is ambiguous by itself: it means either the role is already
            // assigned to this user (the common, correct-and-idempotent case), or the role does not
            // exist in this realm at all (wrong realm, partial import, renamed role — the same
            // silent-failure shape this method exists to fix). Disambiguate against the user's
            // actually-assigned roles instead of guessing.
            if (await UserHasRealmRoleAssignedAsync(userId, realmRole, token, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Keycloak realm role \"{realmRole}\" was not found among user {userId}'s available or " +
                "assigned realm roles — it may not exist in this realm.");
        }

        using var assignRoleRequest = new HttpRequestMessage(
            HttpMethod.Post, $"{AdminRealmBaseUrl}/users/{userId}/role-mappings/realm")
        {
            Content = JsonContent.Create(new JsonArray(role.DeepClone())),
        };
        assignRoleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var assignRoleResponse = await _httpClient.SendAsync(assignRoleRequest, cancellationToken).ConfigureAwait(false);
        assignRoleResponse.EnsureSuccessStatusCode();
    }

    private async Task<bool> UserHasRealmRoleAssignedAsync(string userId, string realmRole, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{AdminRealmBaseUrl}/users/{userId}/role-mappings/realm");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var assignedRoles = await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken).ConfigureAwait(false);
        return assignedRoles?.OfType<JsonObject>().Any(assigned => string.Equals(assigned["name"]?.GetValue<string>(), realmRole, StringComparison.Ordinal)) ?? false;
    }

    // An account still mid-setup (never completed its first login) has UPDATE_PASSWORD pending —
    // safe to keep reissuing a temporary password for it. An account without it has already set
    // its own real password, which must be left alone (see EnsureUserWithTemporaryPasswordAsync).
    private static bool HasPendingPasswordUpdate(JsonObject user) =>
        (user["requiredActions"] as JsonArray)?
            .Any(action => string.Equals(action!.GetValue<string>(), "UPDATE_PASSWORD", StringComparison.Ordinal)) ?? false;

    private async Task PutUserAsync(string userId, JsonObject user, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{AdminRealmBaseUrl}/users/{userId}")
        {
            Content = JsonContent.Create(user),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task ResetPasswordAsync(string userId, string password, string token, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["type"] = "password",
            ["value"] = password,
            ["temporary"] = true,
        };

        using var request = new HttpRequestMessage(HttpMethod.Put, $"{AdminRealmBaseUrl}/users/{userId}/reset-password")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    // Never persisted (R-10) — generated fresh per invitation/resend and handed to the caller to
    // email and discard.
    private static string GenerateTemporaryPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
}
