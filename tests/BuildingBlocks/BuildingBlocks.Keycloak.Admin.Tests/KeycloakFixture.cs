using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Testcontainers.Keycloak;

namespace BuildingBlocks.Keycloak.Admin.Tests;

/// <summary>
/// A real Keycloak (same major as the AppHost and the production image) importing a minimal realm:
/// one confidential client whose service account holds only realm-management manage-users and
/// view-users, plus one realm role that the default roles do not grant. Tests use their own e-mail
/// addresses, so they share the container.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires fixtures used in public test-class constructors (IClassFixture<T>) to be public.")]
public sealed class KeycloakFixture : IAsyncLifetime
{
    internal const string RealmName = "blocktest";
    internal const string ClientId = "block-admin";
    internal const string ClientSecret = "block-admin-secret";
    internal const string GrantableRole = "BLOCK_ROLE";

    private readonly KeycloakContainer _container = new KeycloakBuilder("quay.io/keycloak/keycloak:26.2")
        .WithRealm(Path.Combine(AppContext.BaseDirectory, "blocktest-realm.json"))
        .Build();

    private readonly HttpClient _http = new();

    internal string Authority => $"{_container.GetBaseAddress().ToString().TrimEnd('/')}/realms/{RealmName}";

    private string AdminBase => Authority.Replace("/realms/", "/admin/realms/", StringComparison.Ordinal);

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    internal KeycloakAdminClient CreateClient(string? secret = null)
    {
        var options = Options.Create(new KeycloakAdminOptions
        {
            Authority = Authority,
            AdminClientId = ClientId,
            AdminClientSecret = secret ?? ClientSecret,
        });
        return new KeycloakAdminClient(_http, options);
    }

    internal static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";

    internal async Task<JsonObject?> FindUserAsync(string email) =>
        (await GetAsync<JsonArray>($"users?email={Uri.EscapeDataString(email)}&exact=true"))?.OfType<JsonObject>().FirstOrDefault();

    internal async Task<IReadOnlyList<string>> RealmRolesOfAsync(string userId) =>
        (await GetAsync<JsonArray>($"users/{userId}/role-mappings/realm"))!
            .OfType<JsonObject>().Select(role => role["name"]!.GetValue<string>()).ToList();

    internal async Task<JsonObject> PasswordCredentialAsync(string userId) =>
        (await GetAsync<JsonArray>($"users/{userId}/credentials"))!
            .OfType<JsonObject>().Single(credential => credential["type"]!.GetValue<string>() == "password");

    internal async Task ClearRequiredActionsAsync(JsonObject user)
    {
        user["requiredActions"] = new JsonArray();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{AdminBase}/users/{user["id"]!.GetValue<string>()}")
        {
            Content = JsonContent.Create(user),
        };
        await SendAsync(request);
    }

    private async Task<T?> GetAsync<T>(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{AdminBase}/{path}");
        using var response = await SendAsync(request);
        return await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());
        var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private async Task<string> TokenAsync()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
        });
        using var response = await _http.PostAsync(
            new Uri($"{Authority}/protocol/openid-connect/token", UriKind.Absolute), content, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken);
        return payload!["access_token"]!.GetValue<string>();
    }
}
