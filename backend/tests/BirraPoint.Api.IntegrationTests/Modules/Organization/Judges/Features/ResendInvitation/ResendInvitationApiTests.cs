using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Email;
using BirraPoint.Api.Common.Keycloak;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Judges.Features.ResendInvitation;

/// <summary>POST /competitions/{id}/judges/{judgeId}/invitation: re-triggers delivery.</summary>
public sealed class ResendInvitationApiTests(ApiFactory factory) : JudgesApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ResendInvitationWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var judgeId = await SeedJudgeAsync(competitionId, $"resend-{Guid.NewGuid():N}@brew.example");

        using var judgeClient = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await ResendInvitationAsync(judgeClient, competitionId, judgeId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ResendInvitationReTriggersDeliveryAndReachesSentAgain()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"resend-{Guid.NewGuid():N}@brew.example";

        // ResendInvitation enqueues SendInvitation regardless of current invitationStatus (per
        // contract), so this doesn't depend on RegisterJudges' own (now Pending-only) behavior —
        // the judge id comes straight off the registration response.
        var registerResponse = await RegisterJudgesAsync(organizer, competitionId, email);
        using var registerDocument = JsonDocument.Parse(await registerResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var judgeId = registerDocument.RootElement.GetProperty("created")[0].GetProperty("id").GetGuid();

        var response = await ResendInvitationAsync(organizer, competitionId, judgeId);

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Accepted or HttpStatusCode.NoContent);
        await PollForInvitationStatusAsync(organizer, competitionId, email);
    }

    [Fact]
    public async Task ResendInvitationForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var judgeId = await SeedJudgeAsync(competitionId, $"resend-{Guid.NewGuid():N}@brew.example");

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await ResendInvitationAsync(other, competitionId, judgeId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- GET /judges: US14/FR-057 roster fields -----------------------------------------------
}
