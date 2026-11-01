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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Judges.Features.RegisterJudges;

/// <summary>POST /competitions/{id}/judges: auth/ownership, created vs skipped split, ProvisionJudgeAccount enqueue.</summary>
public sealed class RegisterJudgesApiTests(ApiFactory factory) : JudgesApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task RegisterWithoutABearerTokenIsRejectedWith401()
    {
        using var client = Factory.CreateClient();

        var response = await RegisterJudgesAsync(client, Guid.NewGuid(), "judge@example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await RegisterJudgesAsync(judge, competitionId, "judge@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RegisterForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await RegisterJudgesAsync(other, competitionId, "judge@example.com");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- POST /judges: bulk creation with duplicate/already-registered reporting ------------

    [Fact]
    public async Task RegisterWithAMixedListSplitsCreatedAndSkippedWithReasons()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var existingEmail = $"existing-{Guid.NewGuid():N}@brew.example";
        await SeedJudgeAsync(competitionId, existingEmail);

        var newEmail = $"new-{Guid.NewGuid():N}@brew.example";
        var duplicateInListEmail = $"DUPLICATE-{Guid.NewGuid():N}@brew.example";

        var response = await RegisterJudgesAsync(
            organizer, competitionId,
            newEmail, existingEmail, duplicateInListEmail, duplicateInListEmail.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

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
    public async Task RegisterANewJudgeEnqueuesProvisionJudgeAccountAndTheInvitationStaysPending()
    {
        // R-20/Session 2026-08-02: RegisterJudges (FR-014) no longer auto-sends an invitation —
        // it only provisions the Keycloak account in the background. Status only leaves Pending
        // once the organizer's explicit "Notify judges" action runs (see the Notify_* tests below).
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"async-{Guid.NewGuid():N}@brew.example";

        var response = await RegisterJudgesAsync(organizer, competitionId, email);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await WaitForDispatchJobCompletionAsync(competitionId, DispatchJobType.ProvisionJudgeAccount);

        var listResponse = await GetJudgesAsync(organizer, competitionId);
        using var document = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var judge = document.RootElement.EnumerateArray().Single(j => j.GetProperty("email").GetString() == email);
        Assert.Equal("Pending", judge.GetProperty("invitationStatus").GetString());
    }

    // ---- GET /judges --------------------------------------------------------------------------
}
