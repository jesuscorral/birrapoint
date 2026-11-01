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

namespace BirraPoint.Api.IntegrationTests.Judges.Features.UpdateJudgeEmail;

/// <summary>PUT /competitions/{id}/judges/{judgeId}: email correction before first login, uniqueness, ownership.</summary>
public sealed class UpdateJudgeEmailApiTests(ApiFactory factory) : JudgesApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task UpdateJudgeEmailWithJudgeRoleIsForbiddenWith403()
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
    public async Task UpdateJudgeEmailBeforeFirstLoginSucceeds()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var judgeId = await SeedJudgeAsync(competitionId, $"bounced-{Guid.NewGuid():N}@brew.example", keycloakUserId: null);
        var correctedEmail = $"corrected-{Guid.NewGuid():N}@brew.example";

        var response = await UpdateJudgeEmailAsync(organizer, competitionId, judgeId, correctedEmail);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listResponse = await GetJudgesAsync(organizer, competitionId);
        using var document = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var judges = document.RootElement.EnumerateArray().ToList();
        Assert.Single(judges);
        Assert.Equal(correctedEmail, judges[0].GetProperty("email").GetString());
    }

    [Fact]
    public async Task UpdateJudgeEmailAfterFirstLoginIsRejectedWith409JudgeAlreadyActive()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var judgeId = await SeedJudgeAsync(
            competitionId, $"active-{Guid.NewGuid():N}@brew.example", keycloakUserId: $"kc-{Guid.NewGuid():N}");

        var response = await UpdateJudgeEmailAsync(
            organizer, competitionId, judgeId, $"corrected-{Guid.NewGuid():N}@brew.example");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:judge-already-active", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UpdateJudgeEmailCollidingWithAnotherJudgeInTheSameCompetitionIsRejectedWith400()
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
    public async Task UpdateJudgeEmailForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var judgeId = await SeedJudgeAsync(competitionId, $"bounced-{Guid.NewGuid():N}@brew.example", keycloakUserId: null);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await UpdateJudgeEmailAsync(other, competitionId, judgeId, $"x-{Guid.NewGuid():N}@brew.example");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateJudgeEmailForACompetitionOwnedByADifferentOrganizerReturns404EvenWhenTheEmailIsTakenThere()
    {
        // senior-code-reviewer PR #18 finding: an unscoped uniqueness check would let a caller who
        // doesn't own this competition distinguish 400 (email taken) from 404 (wrong owner) —
        // a scope-existence leak. A non-owner colliding with an email that genuinely IS taken in
        // that competition must still get a plain 404, identical to any other wrong-owner call.
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var takenEmail = $"taken-{Guid.NewGuid():N}@brew.example";
        await SeedJudgeAsync(competitionId, takenEmail);
        var judgeId = await SeedJudgeAsync(competitionId, $"bounced-{Guid.NewGuid():N}@brew.example", keycloakUserId: null);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await UpdateJudgeEmailAsync(other, competitionId, judgeId, takenEmail);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- POST /judges/{judgeId}/invitation: resend --------------------------------------------
}
