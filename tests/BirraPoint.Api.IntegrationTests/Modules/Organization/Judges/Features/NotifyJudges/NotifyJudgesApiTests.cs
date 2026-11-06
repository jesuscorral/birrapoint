using System.Net;
using System.Text.Json;
using BirraPoint.Api.IntegrationTests.TestHost;
using BuildingBlocks.EmailSender;
using BuildingBlocks.Keycloak.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Judges.Features.NotifyJudges;

/// <summary>POST /competitions/{id}/judges/notify (FR-059): enqueues SendInvitation for pending judges only.</summary>
public sealed class NotifyJudgesApiTests(ApiFactory factory) : JudgesApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task NotifyWithoutABearerTokenIsRejectedWith401()
    {
        using var client = Factory.CreateClient();

        var response = await NotifyJudgesAsync(client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NotifyWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await NotifyJudgesAsync(judge, competitionId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NotifyForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await NotifyJudgesAsync(other, competitionId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NotifyIsANoOpWhenNothingIsPending()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);

        var response = await NotifyJudgesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(document.RootElement.GetProperty("queued").EnumerateArray());
    }

    [Fact]
    public async Task NotifyEnqueuesSendInvitationForEveryPendingJudgeAndTheyReachSent()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"notify-{Guid.NewGuid():N}@brew.example";
        await RegisterJudgesAsync(organizer, competitionId, email);

        var response = await NotifyJudgesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var queued = document.RootElement.GetProperty("queued").EnumerateArray().ToList();
        Assert.Single(queued);
        Assert.Equal(email, queued[0].GetProperty("email").GetString());

        await PollForInvitationStatusAsync(organizer, competitionId, email);

        // Literal on purpose: a changed role constant must fail this test (R-10).
        var fakeKeycloak = (FakeKeycloakAdminClient)Factory.Services.GetRequiredService<IKeycloakAdminClient>();
        Assert.Equal("JUDGE", fakeKeycloak.RequestedRole(email));
    }

    [Fact]
    public async Task NotifyDoesNotTouchAJudgeThatIsAlreadySent()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"already-sent-{Guid.NewGuid():N}@brew.example";
        await RegisterJudgesAsync(organizer, competitionId, email);
        await NotifyJudgesAsync(organizer, competitionId);
        await PollForInvitationStatusAsync(organizer, competitionId, email);

        var secondNotify = await NotifyJudgesAsync(organizer, competitionId);

        Assert.Equal(HttpStatusCode.OK, secondNotify.StatusCode);
        using var document = JsonDocument.Parse(await secondNotify.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(document.RootElement.GetProperty("queued").EnumerateArray());
    }

    /// <summary>Regression test: sharing one email across roles/competitions (e.g. an organizer
    /// registering under the same address as a judge elsewhere, per JudgeResolver/
    /// OrganizerResolver) must never reset a Keycloak password some other persona is actively
    /// using — see KeycloakAdminClientTests for the unit-level coverage of the underlying
    /// skip-reset logic.</summary>
    [Fact]
    public async Task NotifyForAnEmailWithAnAlreadyActiveKeycloakAccountDoesNotResetItsPassword()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"shared-identity-{Guid.NewGuid():N}@brew.example";

        var fakeKeycloak = (FakeKeycloakAdminClient)Factory.Services.GetRequiredService<IKeycloakAdminClient>();
        fakeKeycloak.MarkAccountActive(email);

        await RegisterJudgesAsync(organizer, competitionId, email);
        var notifyResponse = await NotifyJudgesAsync(organizer, competitionId);
        Assert.Equal(HttpStatusCode.OK, notifyResponse.StatusCode);

        await PollForInvitationStatusAsync(organizer, competitionId, email);

        Assert.Equal(0, fakeKeycloak.PasswordResetCallCount(email));

        var fakeEmailSender = (FakeEmailSender)Factory.Services.GetRequiredService<IEmailSender>();
        var sentEmail = Assert.Single(fakeEmailSender.Sent, s => s.ToEmail == email);
        Assert.DoesNotContain("temporary password", sentEmail.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("already have a BirraPoint account", sentEmail.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }
}
