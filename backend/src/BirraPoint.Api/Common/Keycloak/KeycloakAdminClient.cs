using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace BirraPoint.Api.Common.Keycloak;

public sealed class KeycloakAdminClient(HttpClient httpClient, IConfiguration configuration) : IKeycloakAdminClient
{
    private readonly string _authority = configuration["Keycloak:Authority"]
        ?? throw new InvalidOperationException("Keycloak:Authority is not configured.");
    private readonly string _clientId = configuration["Keycloak:AdminClientId"]
        ?? throw new InvalidOperationException("Keycloak:AdminClientId is not configured.");
    private readonly string _clientSecret = configuration["Keycloak:AdminClientSecret"]
        ?? throw new InvalidOperationException("Keycloak:AdminClientSecret is not configured.");

    // ".../realms/birrapoint" -> ".../admin/realms/birrapoint" — no separate config key needed.
    private string AdminRealmBaseUrl => _authority.Replace("/realms/", "/admin/realms/");

    public async Task<string> EnsureUserWithTemporaryPasswordAsync(string email, CancellationToken cancellationToken)
    {
        var token = await GetAdminAccessTokenAsync(cancellationToken);
        var existingUser = await FindUserByEmailAsync(email, token, cancellationToken);

        string userId;
        if (existingUser is null)
        {
            userId = await CreateUserAsync(email, token, cancellationToken);
        }
        else
        {
            userId = existingUser["id"]!.GetValue<string>();
            await EnsureUpdatePasswordRequiredActionAsync(existingUser, token, cancellationToken);
        }

        var temporaryPassword = GenerateTemporaryPassword();
        await ResetPasswordAsync(userId, temporaryPassword, token, cancellationToken);
        return temporaryPassword;
    }

    public async Task UpdateUserEmailAsync(string oldEmail, string newEmail, CancellationToken cancellationToken)
    {
        var token = await GetAdminAccessTokenAsync(cancellationToken);
        var existingUser = await FindUserByEmailAsync(oldEmail, token, cancellationToken);
        if (existingUser is null)
        {
            // Not provisioned yet — the pending SendInvitation job reads Judge.Email fresh and
            // will create the Keycloak account under the corrected address directly.
            return;
        }

        var userId = existingUser["id"]!.GetValue<string>();
        existingUser["email"] = newEmail;
        existingUser["username"] = newEmail;

        await PutUserAsync(userId, existingUser, token, cancellationToken);
    }

    private async Task<string> GetAdminAccessTokenAsync(CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
        };

        using var response = await httpClient.PostAsync(
            $"{_authority}/protocol/openid-connect/token", new FormUrlEncodedContent(form), cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return payload?["access_token"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Keycloak token response did not include an access_token.");
    }

    private async Task<JsonObject?> FindUserByEmailAsync(string email, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{AdminRealmBaseUrl}/users?email={Uri.EscapeDataString(email)}&exact=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var users = await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken);
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

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Keycloak's user-create endpoint returns 201 with no body; the new id is the last
        // segment of the Location header.
        var location = response.Headers.Location
            ?? throw new InvalidOperationException("Keycloak user creation response did not include a Location header.");
        return location.Segments[^1];
    }

    private async Task EnsureUpdatePasswordRequiredActionAsync(JsonObject user, string token, CancellationToken cancellationToken)
    {
        var requiredActions = (user["requiredActions"] as JsonArray)?
            .Select(action => action!.GetValue<string>())
            .ToHashSet() ?? [];

        if (requiredActions.Contains("UPDATE_PASSWORD"))
        {
            return;
        }

        requiredActions.Add("UPDATE_PASSWORD");
        user["requiredActions"] = new JsonArray(requiredActions.Select(action => JsonValue.Create(action)).ToArray());

        var userId = user["id"]!.GetValue<string>();
        await PutUserAsync(userId, user, token, cancellationToken);
    }

    private async Task PutUserAsync(string userId, JsonObject user, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{AdminRealmBaseUrl}/users/{userId}")
        {
            Content = JsonContent.Create(user),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, cancellationToken);
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

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    // Never persisted (R-10) — generated fresh per invitation/resend and handed to the caller to
    // email and discard.
    private static string GenerateTemporaryPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
}
