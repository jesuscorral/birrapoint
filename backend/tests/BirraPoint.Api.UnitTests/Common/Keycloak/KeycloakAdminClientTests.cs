using System.Net;
using System.Text;
using System.Text.Json;
using BirraPoint.Api.Common.Keycloak;
using Microsoft.Extensions.Configuration;

namespace BirraPoint.Api.UnitTests.Common.Keycloak;

/// <summary>
/// Covers the JUDGE realm-role grant that <see cref="KeycloakAdminClient.EnsureUserWithTemporaryPasswordAsync"/>
/// must perform for every provisioned judge (new or pre-existing Keycloak user), since
/// `default-roles-birrapoint` never grants JUDGE (infra/keycloak/birrapoint-realm.json).
/// </summary>
public sealed class KeycloakAdminClientTests
{
    private const string Authority = "https://fake-issuer.test/realms/birrapoint";
    private const string AdminRealmBaseUrl = "https://fake-issuer.test/admin/realms/birrapoint";
    private const string NewUserId = "11111111-1111-1111-1111-111111111111";
    private const string ExistingUserId = "22222222-2222-2222-2222-222222222222";

    private static readonly JsonElement JudgeRole =
        JsonDocument.Parse("""{"id":"role-judge-id","name":"JUDGE"}""").RootElement;

    private static KeycloakAdminClient BuildClient(RecordingHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:Authority"] = Authority,
                ["Keycloak:AdminClientId"] = "birrapoint-api-admin",
                ["Keycloak:AdminClientSecret"] = "dev-only-secret-change-me",
            })
            .Build();

        var httpClient = new HttpClient(handler);
        return new KeycloakAdminClient(httpClient, configuration);
    }

    [Fact]
    public async Task New_user_gets_the_JUDGE_role_before_the_password_is_reset()
    {
        var handler = new RecordingHandler();
        handler.When(HttpMethod.Post, "/realms/birrapoint/protocol/openid-connect/token", TokenResponse);
        handler.When(HttpMethod.Get, "/admin/realms/birrapoint/users", _ => JsonResponse(HttpStatusCode.OK, "[]"));
        handler.When(HttpMethod.Post, "/admin/realms/birrapoint/users", _ => CreatedUserResponse(NewUserId));
        handler.When(
            HttpMethod.Get,
            $"/admin/realms/birrapoint/users/{NewUserId}/role-mappings/realm/available",
            _ => JsonResponse(HttpStatusCode.OK, $"[{JudgeRole.GetRawText()}]"));
        handler.When(
            HttpMethod.Post,
            $"/admin/realms/birrapoint/users/{NewUserId}/role-mappings/realm",
            _ => JsonResponse(HttpStatusCode.NoContent, string.Empty));
        handler.When(
            HttpMethod.Put,
            $"/admin/realms/birrapoint/users/{NewUserId}/reset-password",
            _ => JsonResponse(HttpStatusCode.NoContent, string.Empty));

        var client = BuildClient(handler);

        var password = await client.EnsureUserWithTemporaryPasswordAsync("new.judge@example.test", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(password));

        var roleAssignment = handler.Requests.Single(r =>
            r.Method == HttpMethod.Post && r.Path == $"/admin/realms/birrapoint/users/{NewUserId}/role-mappings/realm");
        using var assignedRoles = JsonDocument.Parse(roleAssignment.Body!);
        Assert.Equal(JsonValueKind.Array, assignedRoles.RootElement.ValueKind);
        var assignedRole = Assert.Single(assignedRoles.RootElement.EnumerateArray());
        // Keycloak's role-mappings endpoint requires the role representation's "id", not just its
        // "name" — a body carrying only the name would pass a looser assertion here and then 400
        // against the real API.
        Assert.Equal("JUDGE", assignedRole.GetProperty("name").GetString());
        Assert.Equal("role-judge-id", assignedRole.GetProperty("id").GetString());
        Assert.Equal("Bearer fake-admin-token", roleAssignment.Authorization);

        AssertOrder(
            handler,
            (HttpMethod.Get, "/admin/realms/birrapoint/users"),
            (HttpMethod.Post, "/admin/realms/birrapoint/users"),
            (HttpMethod.Get, $"/admin/realms/birrapoint/users/{NewUserId}/role-mappings/realm/available"),
            (HttpMethod.Post, $"/admin/realms/birrapoint/users/{NewUserId}/role-mappings/realm"),
            (HttpMethod.Put, $"/admin/realms/birrapoint/users/{NewUserId}/reset-password"));
    }

    [Fact]
    public async Task Existing_user_also_gets_the_JUDGE_role_before_the_password_is_reset()
    {
        var handler = new RecordingHandler();
        handler.When(HttpMethod.Post, "/realms/birrapoint/protocol/openid-connect/token", TokenResponse);
        handler.When(
            HttpMethod.Get,
            "/admin/realms/birrapoint/users",
            _ => JsonResponse(HttpStatusCode.OK, ExistingUserJson(requiredActions: "[\"UPDATE_PASSWORD\"]")));
        handler.When(
            HttpMethod.Get,
            $"/admin/realms/birrapoint/users/{ExistingUserId}/role-mappings/realm/available",
            _ => JsonResponse(HttpStatusCode.OK, $"[{JudgeRole.GetRawText()}]"));
        handler.When(
            HttpMethod.Post,
            $"/admin/realms/birrapoint/users/{ExistingUserId}/role-mappings/realm",
            _ => JsonResponse(HttpStatusCode.NoContent, string.Empty));
        handler.When(
            HttpMethod.Put,
            $"/admin/realms/birrapoint/users/{ExistingUserId}/reset-password",
            _ => JsonResponse(HttpStatusCode.NoContent, string.Empty));

        var client = BuildClient(handler);

        var password = await client.EnsureUserWithTemporaryPasswordAsync("existing.judge@example.test", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(password));

        var roleAssignment = handler.Requests.Single(r =>
            r.Method == HttpMethod.Post && r.Path == $"/admin/realms/birrapoint/users/{ExistingUserId}/role-mappings/realm");
        using var assignedRoles = JsonDocument.Parse(roleAssignment.Body!);
        Assert.Contains(
            assignedRoles.RootElement.EnumerateArray(),
            role => role.GetProperty("name").GetString() == "JUDGE");

        AssertOrder(
            handler,
            (HttpMethod.Get, "/admin/realms/birrapoint/users"),
            (HttpMethod.Get, $"/admin/realms/birrapoint/users/{ExistingUserId}/role-mappings/realm/available"),
            (HttpMethod.Post, $"/admin/realms/birrapoint/users/{ExistingUserId}/role-mappings/realm"),
            (HttpMethod.Put, $"/admin/realms/birrapoint/users/{ExistingUserId}/reset-password"));
    }

    /// <summary>
    /// Covers sharing a Keycloak account across roles/competitions (one email organizing
    /// competition A and judging competition B, per JudgeResolver/OrganizerResolver): once the
    /// account has completed its own UPDATE_PASSWORD setup, granting JUDGE must never reset (and
    /// therefore never invalidate) a password some other persona is actively using elsewhere.
    /// </summary>
    [Fact]
    public async Task Existing_active_user_keeps_JUDGE_role_but_skips_the_password_reset()
    {
        var handler = new RecordingHandler();
        handler.When(HttpMethod.Post, "/realms/birrapoint/protocol/openid-connect/token", TokenResponse);
        handler.When(
            HttpMethod.Get,
            "/admin/realms/birrapoint/users",
            _ => JsonResponse(HttpStatusCode.OK, ExistingUserJson(requiredActions: "[]")));
        handler.When(
            HttpMethod.Get,
            $"/admin/realms/birrapoint/users/{ExistingUserId}/role-mappings/realm/available",
            _ => JsonResponse(HttpStatusCode.OK, $"[{JudgeRole.GetRawText()}]"));
        handler.When(
            HttpMethod.Post,
            $"/admin/realms/birrapoint/users/{ExistingUserId}/role-mappings/realm",
            _ => JsonResponse(HttpStatusCode.NoContent, string.Empty));

        var client = BuildClient(handler);

        var password = await client.EnsureUserWithTemporaryPasswordAsync("active.organizer@example.test", CancellationToken.None);

        Assert.Null(password);

        var roleAssignment = handler.Requests.Single(r =>
            r.Method == HttpMethod.Post && r.Path == $"/admin/realms/birrapoint/users/{ExistingUserId}/role-mappings/realm");
        using var assignedRoles = JsonDocument.Parse(roleAssignment.Body!);
        Assert.Contains(
            assignedRoles.RootElement.EnumerateArray(),
            role => role.GetProperty("name").GetString() == "JUDGE");

        Assert.DoesNotContain(
            handler.Requests,
            r => r.Method == HttpMethod.Put && r.Path.EndsWith("/reset-password", StringComparison.Ordinal));
        Assert.DoesNotContain(
            handler.Requests,
            r => r.Method == HttpMethod.Put && r.Path == $"/admin/realms/birrapoint/users/{ExistingUserId}");
    }

    [Fact]
    public async Task Already_having_JUDGE_assigned_is_a_no_op_and_still_resets_the_password()
    {
        var handler = new RecordingHandler();
        handler.When(HttpMethod.Post, "/realms/birrapoint/protocol/openid-connect/token", TokenResponse);
        handler.When(
            HttpMethod.Get,
            "/admin/realms/birrapoint/users",
            _ => JsonResponse(HttpStatusCode.OK, ExistingUserJson(requiredActions: "[\"UPDATE_PASSWORD\"]")));
        handler.When(
            HttpMethod.Get,
            $"/admin/realms/birrapoint/users/{ExistingUserId}/role-mappings/realm/available",
            _ => JsonResponse(HttpStatusCode.OK, """[{"id":"role-organizer-id","name":"ORGANIZER"}]"""));
        handler.When(
            HttpMethod.Get,
            $"/admin/realms/birrapoint/users/{ExistingUserId}/role-mappings/realm",
            // Absent from "available" is disambiguated against the user's actually-assigned
            // roles — JUDGE showing up here (not just absent from "available") is what makes this
            // genuinely a no-op rather than a misconfigured realm (see the next test).
            _ => JsonResponse(HttpStatusCode.OK, $"[{JudgeRole.GetRawText()}]"));
        handler.When(
            HttpMethod.Put,
            $"/admin/realms/birrapoint/users/{ExistingUserId}/reset-password",
            _ => JsonResponse(HttpStatusCode.NoContent, string.Empty));

        var client = BuildClient(handler);

        var password = await client.EnsureUserWithTemporaryPasswordAsync("already.judge@example.test", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(password));
        Assert.DoesNotContain(
            handler.Requests,
            r => r.Method == HttpMethod.Post && r.Path.EndsWith("/role-mappings/realm", StringComparison.Ordinal));
        Assert.Contains(
            handler.Requests,
            r => r.Method == HttpMethod.Put && r.Path == $"/admin/realms/birrapoint/users/{ExistingUserId}/reset-password");
    }

    [Fact]
    public async Task Judge_role_missing_from_the_realm_entirely_throws_instead_of_silently_no_opping()
    {
        var handler = new RecordingHandler();
        handler.When(HttpMethod.Post, "/realms/birrapoint/protocol/openid-connect/token", TokenResponse);
        handler.When(
            HttpMethod.Get,
            "/admin/realms/birrapoint/users",
            _ => JsonResponse(HttpStatusCode.OK, ExistingUserJson(requiredActions: "[\"UPDATE_PASSWORD\"]")));
        handler.When(
            HttpMethod.Get,
            $"/admin/realms/birrapoint/users/{ExistingUserId}/role-mappings/realm/available",
            _ => JsonResponse(HttpStatusCode.OK, """[{"id":"role-organizer-id","name":"ORGANIZER"}]"""));
        handler.When(
            HttpMethod.Get,
            $"/admin/realms/birrapoint/users/{ExistingUserId}/role-mappings/realm",
            // Absent here too — a misconfigured/mismatched realm, not "already assigned" — must
            // not be swallowed the same way the original bug silently produced a role-less judge.
            _ => JsonResponse(HttpStatusCode.OK, """[{"id":"role-organizer-id","name":"ORGANIZER"}]"""));

        var client = BuildClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.EnsureUserWithTemporaryPasswordAsync("misconfigured.judge@example.test", CancellationToken.None));

        Assert.DoesNotContain(
            handler.Requests,
            r => r.Method == HttpMethod.Put && r.Path.EndsWith("/reset-password", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Role_assignment_failure_propagates_and_the_password_is_never_reset()
    {
        var handler = new RecordingHandler();
        handler.When(HttpMethod.Post, "/realms/birrapoint/protocol/openid-connect/token", TokenResponse);
        handler.When(HttpMethod.Get, "/admin/realms/birrapoint/users", _ => JsonResponse(HttpStatusCode.OK, "[]"));
        handler.When(HttpMethod.Post, "/admin/realms/birrapoint/users", _ => CreatedUserResponse(NewUserId));
        handler.When(
            HttpMethod.Get,
            $"/admin/realms/birrapoint/users/{NewUserId}/role-mappings/realm/available",
            _ => JsonResponse(HttpStatusCode.OK, $"[{JudgeRole.GetRawText()}]"));
        handler.When(
            HttpMethod.Post,
            $"/admin/realms/birrapoint/users/{NewUserId}/role-mappings/realm",
            _ => JsonResponse(HttpStatusCode.Forbidden, string.Empty));

        var client = BuildClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.EnsureUserWithTemporaryPasswordAsync("forbidden.judge@example.test", CancellationToken.None));

        Assert.DoesNotContain(
            handler.Requests,
            r => r.Method == HttpMethod.Put && r.Path.EndsWith("/reset-password", StringComparison.Ordinal));
    }

    private static void AssertOrder(RecordingHandler handler, params (HttpMethod Method, string Path)[] expected)
    {
        var actual = handler.Requests
            .Where(r => expected.Any(e => e.Method == r.Method && e.Path == r.Path))
            .Select(r => (r.Method, r.Path))
            .ToList();

        Assert.Equal(expected, actual);
    }

    private static string ExistingUserJson(string requiredActions) =>
        $$"""[{"id":"{{ExistingUserId}}","email":"existing.judge@example.test","username":"existing.judge@example.test","requiredActions":{{requiredActions}}}]""";

    private static Task<HttpResponseMessage> TokenResponse(HttpRequestMessage _) =>
        JsonResponse(HttpStatusCode.OK, """{"access_token":"fake-admin-token"}""");

    private static Task<HttpResponseMessage> CreatedUserResponse(string userId)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Created);
        response.Headers.Location = new Uri($"{AdminRealmBaseUrl}/users/{userId}");
        return Task.FromResult(response);
    }

    private static Task<HttpResponseMessage> JsonResponse(HttpStatusCode statusCode, string json)
    {
        var response = new HttpResponseMessage(statusCode);
        if (!string.IsNullOrEmpty(json))
        {
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return Task.FromResult(response);
    }

    /// <summary>Records every request (method, path, body) in call order and dispatches to a registered stub by method+path.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Dictionary<(HttpMethod Method, string Path), Func<HttpRequestMessage, Task<HttpResponseMessage>>> _stubs = [];

        public List<(HttpMethod Method, string Path, string? Body, string? Authorization)> Requests { get; } = [];

        public void When(HttpMethod method, string path, Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
            _stubs[(method, path)] = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, path, body, request.Headers.Authorization?.ToString()));

            if (!_stubs.TryGetValue((request.Method, path), out var respond))
            {
                throw new InvalidOperationException($"No stub registered for {request.Method} {path}.");
            }

            return await respond(request);
        }
    }
}
