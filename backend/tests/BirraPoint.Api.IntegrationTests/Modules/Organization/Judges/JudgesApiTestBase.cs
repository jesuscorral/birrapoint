using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Email;
using BirraPoint.Api.Common.Keycloak;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Judges;

/// <summary>Shared clients, HTTP helpers, seeding and dispatch polling for the Judges slice
/// contract tests (contracts/rest-api.md §Judges).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "Base class of public xUnit test classes; C# requires a base class to be at least as accessible as its derived classes.")]
public abstract class JudgesApiTestBase(ApiFactory factory)
{
    private protected ApiFactory Factory => factory;

    private protected static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
    private protected static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private protected HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    private protected HttpClient JudgeClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "JUDGE"));
        return client;
    }

    private protected static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Judges")
    {
        var response = await client.PostAsJsonAsync("/api/v1/competitions", new
        {
            name = $"{namePrefix} {Guid.NewGuid():N}",
            venue = "Centro de Convenciones",
            startDate = "2026-08-01",
            endDate = "2026-08-03",
        });
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

    private protected static Task<HttpResponseMessage> RegisterJudgesAsync(HttpClient client, Guid competitionId, params string[] emails) =>
        client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/judges", new { emails });

    private protected static Task<HttpResponseMessage> GetJudgesAsync(HttpClient client, Guid competitionId) =>
        client.GetAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/judges"));

    private protected static Task<HttpResponseMessage> UpdateJudgeEmailAsync(HttpClient client, Guid competitionId, Guid judgeId, string email) =>
        client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/judges/{judgeId}", new { email });

    private protected static Task<HttpResponseMessage> ResendInvitationAsync(HttpClient client, Guid competitionId, Guid judgeId) =>
        client.PostAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/judges/{judgeId}/invitation"), null);

    private protected static Task<HttpResponseMessage> NotifyJudgesAsync(HttpClient client, Guid competitionId) =>
        client.PostAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/judges/notify"), null);

    /// <summary>Seeds a Judge row (plus its required Invitation row — RegisterJudges always
    /// creates one alongside the Judge, and handlers assume it exists) directly via AppDbContext.
    /// This is the only way to exercise the judge-already-active gate in a test, since
    /// KeycloakUserId is set exclusively by a real Keycloak login through JudgeResolver
    /// and there is no way to make a fake JWT "log in" as a pre-existing judge (its sub can't be
    /// derived ahead of time).</summary>
    private protected async Task<Guid> SeedJudgeAsync(Guid competitionId, string email, string? keycloakUserId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judge = new Judge
        {
            CompetitionId = competitionId,
            Email = email,
            DisplayName = email.Split('@')[0],
            KeycloakUserId = keycloakUserId,
        };
        db.Judges.Add(judge);
        db.Invitations.Add(new Invitation { JudgeId = judge.Id });
        await db.SaveChangesAsync();
        return judge.Id;
    }

    /// <summary>Polls GET /judges until the named judge's invitationStatus leaves "Pending" —
    /// the invitation is dispatched asynchronously via a DispatchJob picked up by the live
    /// DispatchWorker background service, so it must not be asserted on synchronously
    /// right after POST /judges.</summary>
    private protected static async Task<JsonElement> PollForInvitationStatusAsync(
        HttpClient client, Guid competitionId, string email, string expectedStatus = "Sent")
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var response = await GetJudgesAsync(client, competitionId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var judge = document.RootElement.EnumerateArray()
                .FirstOrDefault(j => j.GetProperty("email").GetString() == email);

            if (judge.ValueKind == JsonValueKind.Object
                && judge.TryGetProperty("invitationStatus", out var status)
                && status.GetString() == expectedStatus)
            {
                return judge.Clone();
            }

            await Task.Delay(PollInterval);
        }

        Assert.Fail($"Timed out waiting for {email}'s invitationStatus to reach '{expectedStatus}'.");
        throw new UnreachableException();
    }

    /// <summary>Polls until a <see cref="DispatchJobType"/> DispatchJob for this competition
    /// reaches <see cref="DispatchJobStatus.Completed"/> — used to synchronize with
    /// ProvisionJudgeAccountHandler, which (unlike SendInvitationHandler) leaves no
    /// judge/invitation-row side effect to poll for instead (R-20).</summary>
    private protected async Task WaitForDispatchJobCompletionAsync(Guid competitionId, DispatchJobType type)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var completed = await db.DispatchJobs.AnyAsync(j =>
                j.CompetitionId == competitionId && j.Type == type && j.Status == DispatchJobStatus.Completed);

            if (completed)
            {
                return;
            }

            await Task.Delay(PollInterval);
        }

        Assert.Fail($"Timed out waiting for a {type} DispatchJob to complete for competition {competitionId}.");
    }

    // ---- POST /judges: auth & ownership -----------------------------------------------------
}
