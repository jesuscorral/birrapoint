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

namespace BirraPoint.Api.IntegrationTests.Judges.Features.GetJudges;

/// <summary>GET /competitions/{id}/judges: auth/ownership, profiles with invitation delivery status.</summary>
public sealed class GetJudgesApiTests(ApiFactory factory) : JudgesApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task GetJudgesWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await GetJudgesAsync(judge, competitionId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetJudgesForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await GetJudgesAsync(other, competitionId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetJudgesReturnsTheRegisteredProfilesWithDeliveryStatus()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"list-{Guid.NewGuid():N}@brew.example";

        await RegisterJudgesAsync(organizer, competitionId, email);

        var response = await GetJudgesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var judges = document.RootElement.EnumerateArray().ToList();
        Assert.Single(judges);
        Assert.Equal(email, judges[0].GetProperty("email").GetString());
        Assert.True(judges[0].TryGetProperty("invitationStatus", out _));
    }

    // ---- PUT /judges/{judgeId}: email correction ----------------------------------------------

    [Fact]
    public async Task GetJudgesResponseIncludesTheFourRosterFieldsNullForEmailListCreatedJudges()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"roster-null-{Guid.NewGuid():N}@brew.example";

        await RegisterJudgesAsync(organizer, competitionId, email);

        var response = await GetJudgesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var judge = document.RootElement.EnumerateArray().Single(j => j.GetProperty("email").GetString() == email);
        Assert.Equal(JsonValueKind.Null, judge.GetProperty("bjcpRank").ValueKind);
        Assert.Equal(JsonValueKind.Null, judge.GetProperty("bjcpId").ValueKind);
        Assert.Equal(JsonValueKind.Null, judge.GetProperty("preferredCategory").ValueKind);
        Assert.Equal(JsonValueKind.Null, judge.GetProperty("preferences").ValueKind);
    }

    // ---- POST /judges/notify: FR-059 explicit "Notify judges" action --------------------------
}
