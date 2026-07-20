using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Judges;

/// <summary>
/// T039: HTTP-level contract tests for the Judges slices (contracts/rest-api.md §Judges) —
/// bulk registration with duplicate/already-registered reporting (FR-014/FR-015), invitation
/// delivery status via the async DispatchJob pipeline (T040-T042, in progress in parallel;
/// failures against non-existent routes are expected until that work lands), email correction
/// with the judge-already-active gate, and invitation resend.
/// </summary>
public sealed class JudgesApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private HttpClient OrganizerClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "ORGANIZER"));
        return client;
    }

    private HttpClient JudgeClient(string sub)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtIssuer.IssueToken(sub, null, "JUDGE"));
        return client;
    }

    private static async Task<Guid> CreateCompetitionAsync(HttpClient client, string namePrefix = "Judges")
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

    private static Task<HttpResponseMessage> RegisterJudgesAsync(HttpClient client, Guid competitionId, params string[] emails) =>
        client.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/judges", new { emails });

    private static Task<HttpResponseMessage> GetJudgesAsync(HttpClient client, Guid competitionId) =>
        client.GetAsync($"/api/v1/competitions/{competitionId}/judges");

    private static Task<HttpResponseMessage> UpdateJudgeEmailAsync(HttpClient client, Guid competitionId, Guid judgeId, string email) =>
        client.PutAsJsonAsync($"/api/v1/competitions/{competitionId}/judges/{judgeId}", new { email });

    private static Task<HttpResponseMessage> ResendInvitationAsync(HttpClient client, Guid competitionId, Guid judgeId) =>
        client.PostAsync($"/api/v1/competitions/{competitionId}/judges/{judgeId}/invitation", null);

    /// <summary>Seeds a Judge row (plus its required Invitation row — RegisterJudges always
    /// creates one alongside the Judge, and handlers assume it exists) directly via AppDbContext.
    /// This is the only way to exercise the judge-already-active gate in a test, since
    /// KeycloakUserId is set exclusively by a real Keycloak login through JudgeResolver (T023)
    /// and there is no way to make a fake JWT "log in" as a pre-existing judge (its sub can't be
    /// derived ahead of time).</summary>
    private async Task<Guid> SeedJudgeAsync(Guid competitionId, string email, string? keycloakUserId = null)
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
    /// DispatchWorker background service (T016), so it must not be asserted on synchronously
    /// right after POST /judges.</summary>
    private static async Task<JsonElement> PollForInvitationStatusAsync(
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

    // ---- POST /judges: auth & ownership -----------------------------------------------------

    [Fact]
    public async Task Register_without_a_bearer_token_is_rejected_with_401()
    {
        using var client = factory.CreateClient();

        var response = await RegisterJudgesAsync(client, Guid.NewGuid(), "judge@example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await RegisterJudgesAsync(judge, competitionId, "judge@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Register_for_a_competition_owned_by_a_different_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await RegisterJudgesAsync(other, competitionId, "judge@example.com");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- POST /judges: bulk creation with duplicate/already-registered reporting ------------

    [Fact]
    public async Task Register_with_a_mixed_list_splits_created_and_skipped_with_reasons()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var existingEmail = $"existing-{Guid.NewGuid():N}@brew.example";
        await SeedJudgeAsync(competitionId, existingEmail);

        var newEmail = $"new-{Guid.NewGuid():N}@brew.example";
        var duplicateInListEmail = $"DUPLICATE-{Guid.NewGuid():N}@brew.example";

        var response = await RegisterJudgesAsync(
            organizer, competitionId,
            newEmail, existingEmail, duplicateInListEmail, duplicateInListEmail.ToLowerInvariant());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var created = document.RootElement.GetProperty("created").EnumerateArray().ToList();
        var skipped = document.RootElement.GetProperty("skipped").EnumerateArray().ToList();

        // newEmail + one occurrence of the case-insensitive duplicate pair are created;
        // existingEmail (already-registered) and the second duplicate occurrence are skipped.
        Assert.Equal(2, created.Count);
        Assert.Contains(created, c => c.GetProperty("email").GetString() == newEmail);
        Assert.All(created, c => Assert.NotEqual(Guid.Empty, c.GetProperty("id").GetGuid()));

        Assert.Equal(2, skipped.Count);
        Assert.Contains(skipped, s =>
            s.GetProperty("email").GetString() == existingEmail
            && s.GetProperty("reason").GetString() == "already-registered");
        Assert.Contains(skipped, s => s.GetProperty("reason").GetString() == "duplicate-in-list");
    }

    [Fact]
    public async Task Register_a_new_judge_reaches_Sent_invitation_status_via_the_async_dispatch_pipeline()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"async-{Guid.NewGuid():N}@brew.example";

        var response = await RegisterJudgesAsync(organizer, competitionId, email);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var judge = await PollForInvitationStatusAsync(organizer, competitionId, email);
        Assert.False(string.IsNullOrWhiteSpace(judge.GetProperty("id").GetString()));
    }

    // ---- GET /judges --------------------------------------------------------------------------

    [Fact]
    public async Task Get_judges_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await GetJudgesAsync(judge, competitionId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_judges_for_a_competition_owned_by_a_different_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await GetJudgesAsync(other, competitionId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_judges_returns_the_registered_profiles_with_delivery_status()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"list-{Guid.NewGuid():N}@brew.example";

        await RegisterJudgesAsync(organizer, competitionId, email);

        var response = await GetJudgesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var judges = document.RootElement.EnumerateArray().ToList();
        Assert.Single(judges);
        Assert.Equal(email, judges[0].GetProperty("email").GetString());
        Assert.True(judges[0].TryGetProperty("invitationStatus", out _));
    }

    // ---- PUT /judges/{judgeId}: email correction ----------------------------------------------

    [Fact]
    public async Task Update_judge_email_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var judgeId = await SeedJudgeAsync(competitionId, $"bounced-{Guid.NewGuid():N}@brew.example");

        using var judgeClient = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await UpdateJudgeEmailAsync(
            judgeClient, competitionId, judgeId, $"corrected-{Guid.NewGuid():N}@brew.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_judge_email_before_first_login_succeeds()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var judgeId = await SeedJudgeAsync(competitionId, $"bounced-{Guid.NewGuid():N}@brew.example", keycloakUserId: null);
        var correctedEmail = $"corrected-{Guid.NewGuid():N}@brew.example";

        var response = await UpdateJudgeEmailAsync(organizer, competitionId, judgeId, correctedEmail);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listResponse = await GetJudgesAsync(organizer, competitionId);
        using var document = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var judges = document.RootElement.EnumerateArray().ToList();
        Assert.Single(judges);
        Assert.Equal(correctedEmail, judges[0].GetProperty("email").GetString());
    }

    [Fact]
    public async Task Update_judge_email_after_first_login_is_rejected_with_409_judge_already_active()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var judgeId = await SeedJudgeAsync(
            competitionId, $"active-{Guid.NewGuid():N}@brew.example", keycloakUserId: $"kc-{Guid.NewGuid():N}");

        var response = await UpdateJudgeEmailAsync(
            organizer, competitionId, judgeId, $"corrected-{Guid.NewGuid():N}@brew.example");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("urn:birrapoint:judge-already-active", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Update_judge_email_colliding_with_another_judge_in_the_same_competition_is_rejected_with_400()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var takenEmail = $"taken-{Guid.NewGuid():N}@brew.example";
        await SeedJudgeAsync(competitionId, takenEmail);
        var judgeId = await SeedJudgeAsync(competitionId, $"bounced-{Guid.NewGuid():N}@brew.example", keycloakUserId: null);

        var response = await UpdateJudgeEmailAsync(organizer, competitionId, judgeId, takenEmail);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_judge_email_for_a_competition_owned_by_a_different_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var judgeId = await SeedJudgeAsync(competitionId, $"bounced-{Guid.NewGuid():N}@brew.example", keycloakUserId: null);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await UpdateJudgeEmailAsync(other, competitionId, judgeId, $"x-{Guid.NewGuid():N}@brew.example");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- POST /judges/{judgeId}/invitation: resend --------------------------------------------

    [Fact]
    public async Task Resend_invitation_with_judge_role_is_forbidden_with_403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var judgeId = await SeedJudgeAsync(competitionId, $"resend-{Guid.NewGuid():N}@brew.example");

        using var judgeClient = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await ResendInvitationAsync(judgeClient, competitionId, judgeId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Resend_invitation_re_triggers_delivery_and_reaches_Sent_again()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"resend-{Guid.NewGuid():N}@brew.example";

        await RegisterJudgesAsync(organizer, competitionId, email);
        var firstAttempt = await PollForInvitationStatusAsync(organizer, competitionId, email);
        var judgeId = firstAttempt.GetProperty("id").GetGuid();

        var response = await ResendInvitationAsync(organizer, competitionId, judgeId);

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted or HttpStatusCode.NoContent);
        await PollForInvitationStatusAsync(organizer, competitionId, email);
    }

    [Fact]
    public async Task Resend_invitation_for_a_competition_owned_by_a_different_organizer_returns_404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var judgeId = await SeedJudgeAsync(competitionId, $"resend-{Guid.NewGuid():N}@brew.example");

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await ResendInvitationAsync(other, competitionId, judgeId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
