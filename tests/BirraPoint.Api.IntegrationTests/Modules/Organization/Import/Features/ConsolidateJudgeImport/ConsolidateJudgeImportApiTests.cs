using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.TestHost;
using BirraPoint.Api.Modules.Organization.Domain;
using BuildingBlocks.Jobs;
using BuildingBlocks.Keycloak.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BirraPoint.Api.IntegrationTests.Modules.Organization.Import.Features.ConsolidateJudgeImport;

/// <summary>
/// HTTP-level contract tests for POST /competitions/{id}/imports/judges/{importId}/consolidate (contracts/rest-api.md §Judge Roster Import) — 409 unresolved-import-rows gating, judge create/update, ProvisionJudgeAccount (never SendInvitation, R-20) enqueueing.
/// </summary>
public sealed class ConsolidateJudgeImportApiTests(ApiFactory factory) : ImportApiTestBase(factory), IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ConsolidateWithJudgeRoleIsForbiddenWith403()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow());

        using var judge = JudgeClient($"judge-{Guid.NewGuid():N}");
        var response = await ConsolidateJudgeAsync(judge, competitionId, importId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ConsolidateIsBlockedWith409UnresolvedImportRowsWhileAnyRowIsInvalid()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(
            organizer, competitionId, JudgeRow(name: "Ana Gomez"), JudgeRow(name: "Sam Roe", email: null));

        var response = await ConsolidateJudgeAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:unresolved-import-rows", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ConsolidateASecondTimeOnTheSameBatchIsRejectedWith409()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow());

        var first = await ConsolidateJudgeAsync(organizer, competitionId, importId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await ConsolidateJudgeAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var document = JsonDocument.Parse(await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("urn:birrapoint:invalid-state-transition", document.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ConsolidateForACompetitionOwnedByADifferentOrganizerReturns404()
    {
        using var owner = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(owner);
        var (importId, _) = await UploadJudgeRowsAsync(owner, competitionId, JudgeRow());

        using var other = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var response = await ConsolidateJudgeAsync(other, competitionId, importId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConsolidateSuccessCreatesAJudgeWithEveryRosterFieldPersisted()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"rebeca-{Guid.NewGuid():N}@brew.example";
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(email: email));

        var response = await ConsolidateJudgeAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var created = document.RootElement.GetProperty("created").EnumerateArray().ToList();
        Assert.Single(created);
        var judgeId = created[0].GetProperty("id").GetGuid();

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judge = await db.Judges.SingleAsync(j => j.Id == judgeId, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(email, judge.Email);
        Assert.Equal("Ana García Ruiz", judge.DisplayName);
        Assert.Equal("Certificado", judge.BjcpRank);
        Assert.Equal("E4612", judge.BjcpId);
        Assert.Equal("Estilos Clásicos", judge.PreferredCategory);

        var invitation = await db.Invitations.SingleAsync(i => i.JudgeId == judgeId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(InvitationStatus.Pending, invitation.Status);
    }

    [Fact]
    public async Task ConsolidateSuccessEnqueuesProvisionJudgeAccountAndNeverSendInvitation()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"provision-{Guid.NewGuid():N}@brew.example";
        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(email: email));

        var response = await ConsolidateJudgeAsync(organizer, competitionId, importId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var judgeId = document.RootElement.GetProperty("created")[0].GetProperty("id").GetGuid();

        await WaitForDispatchJobCompletionAsync(competitionId, DispatchJobType.ProvisionJudgeAccount);

        Assert.False(await HasDispatchJobAsync(competitionId, DispatchJobType.SendInvitation));

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invitation = await db.Invitations.SingleAsync(i => i.JudgeId == judgeId, cancellationToken: TestContext.Current.CancellationToken);
        // Provisioning never sends the invitation email — status stays Pending until the
        // organizer's explicit "Notify judges" action (FR-059).
        Assert.Equal(InvitationStatus.Pending, invitation.Status);
    }

    [Fact]
    public async Task ConsolidateReimportingTheSameEmailWithinOneCompetitionUpdatesTheExistingJudge()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"repeat-{Guid.NewGuid():N}@brew.example";

        var (firstImportId, _) = await UploadJudgeRowsAsync(
            organizer, competitionId, JudgeRow(name: "Original Name", email: email, bjcpRank: "Pendiente de Rango"));
        var firstConsolidate = await ConsolidateJudgeAsync(organizer, competitionId, firstImportId);
        Assert.Equal(HttpStatusCode.OK, firstConsolidate.StatusCode);

        var (secondImportId, _) = await UploadJudgeRowsAsync(
            organizer, competitionId, JudgeRow(name: "Updated Name", email: email, bjcpRank: "Certificado"));
        var secondConsolidate = await ConsolidateJudgeAsync(organizer, competitionId, secondImportId);

        Assert.Equal(HttpStatusCode.OK, secondConsolidate.StatusCode);
        using var document = JsonDocument.Parse(await secondConsolidate.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, document.RootElement.GetProperty("created").GetArrayLength());
        var updated = document.RootElement.GetProperty("updated").EnumerateArray().ToList();
        Assert.Single(updated);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judges = await db.Judges.Where(j => j.CompetitionId == competitionId && j.Email == email).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(judges);
        Assert.Equal("Updated Name", judges[0].DisplayName);
        Assert.Equal("Certificado", judges[0].BjcpRank);
    }

    /// <summary>Regression test for the account-lockout bug: re-provisioning an already-notified
    /// judge must never reset their live password (research.md R-20). Covers both the re-import
    /// path (ConsolidateJudgeImport's enqueue-time narrowing) and the retry-reordering race
    /// (ProvisionJudgeAccountHandler's own Invitation.Status guard, exercised here by enqueueing
    /// directly so the assertion doesn't depend on the narrowing alone).</summary>
    [Fact]
    public async Task ProvisionJudgeAccountDoesNotResetThePasswordOnceTheJudgeHasAlreadyBeenNotified()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"already-notified-{Guid.NewGuid():N}@brew.example";

        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(name: "Original Name", email: email));
        var consolidate = await ConsolidateJudgeAsync(organizer, competitionId, importId);
        Assert.Equal(HttpStatusCode.OK, consolidate.StatusCode);
        await WaitForDispatchJobCompletionAsync(competitionId, DispatchJobType.ProvisionJudgeAccount);

        var notifyResponse = await organizer.PostAsync(Urls.Rel($"/api/v1/competitions/{competitionId}/judges/notify"), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, notifyResponse.StatusCode);
        await WaitForDispatchJobCompletionAsync(competitionId, DispatchJobType.SendInvitation);

        var fakeKeycloak = (FakeKeycloakAdminClient)Factory.Services.GetRequiredService<IKeycloakAdminClient>();
        var callCountAfterNotify = fakeKeycloak.PasswordResetCallCount(email);
        Assert.True(callCountAfterNotify >= 1);

        Guid judgeId;
        await using (var seedScope = Factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            judgeId = await db.Judges
                .Where(j => j.CompetitionId == competitionId && j.Email == email)
                .Select(j => j.Id)
                .SingleAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(InvitationStatus.Sent, (await db.Invitations.SingleAsync(i => i.JudgeId == judgeId, cancellationToken: TestContext.Current.CancellationToken)).Status);
        }

        // Simulates a stale re-enqueue of ProvisionJudgeAccount for a judge who has already been
        // notified — e.g. a re-import consolidation, or a backed-off retry racing a
        // since-completed "Notify judges" send (research.md R-20).
        await using (var enqueueScope = Factory.Services.CreateAsyncScope())
        {
            var dispatchJobQueue = enqueueScope.ServiceProvider.GetRequiredService<IJobQueue>();
            await dispatchJobQueue.EnqueueAsync(competitionId, DispatchJobType.ProvisionJudgeAccount, new { JudgeId = judgeId }, TestContext.Current.CancellationToken);
        }

        await WaitForDispatchJobCompletionAsync(competitionId, DispatchJobType.ProvisionJudgeAccount, expectedCount: 2);

        Assert.Equal(callCountAfterNotify, fakeKeycloak.PasswordResetCallCount(email));
    }

    [Fact]
    public async Task ConsolidateDuplicateEmailsWithinTheSameFileResolveToASingleUpsert()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var sharedEmail = $"shared-{Guid.NewGuid():N}@brew.example";

        var (importId, _) = await UploadJudgeRowsAsync(
            organizer, competitionId,
            JudgeRow(name: "First Occurrence", email: sharedEmail),
            JudgeRow(name: "Second Occurrence", email: sharedEmail));

        var response = await ConsolidateJudgeAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, document.RootElement.GetProperty("created").GetArrayLength());
        var skipped = document.RootElement.GetProperty("skipped").EnumerateArray().ToList();
        Assert.Single(skipped);
        Assert.Equal(sharedEmail, skipped[0].GetProperty("email").GetString());
        Assert.Equal("duplicate-in-list", skipped[0].GetProperty("reason").GetString());

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judges = await db.Judges.Where(j => j.CompetitionId == competitionId && j.Email == sharedEmail).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(judges);
        // Last-import-wins within the batch — the second occurrence's values win.
        Assert.Equal("Second Occurrence", judges[0].DisplayName);
    }

    [Fact]
    public async Task ConsolidateUpdatingAJudgeCreatedViaTheEmailListFlowDoesNotDuplicateTheInvitation()
    {
        using var organizer = OrganizerClient($"organizer-{Guid.NewGuid():N}");
        var competitionId = await CreateCompetitionAsync(organizer);
        var email = $"already-registered-{Guid.NewGuid():N}@brew.example";

        // Seed via the plain email-list flow (RegisterJudges) first — this already creates an
        // Invitation{Status=Pending} row.
        await organizer.PostAsJsonAsync($"/api/v1/competitions/{competitionId}/judges", new { emails = new[] { email } }, cancellationToken: TestContext.Current.CancellationToken);

        var (importId, _) = await UploadJudgeRowsAsync(organizer, competitionId, JudgeRow(name: "Roster Name", email: email));
        var response = await ConsolidateJudgeAsync(organizer, competitionId, importId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, document.RootElement.GetProperty("created").GetArrayLength());
        Assert.Equal(1, document.RootElement.GetProperty("updated").GetArrayLength());

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var judge = await db.Judges.SingleAsync(j => j.CompetitionId == competitionId && j.Email == email, cancellationToken: TestContext.Current.CancellationToken);
        var invitations = await db.Invitations.Where(i => i.JudgeId == judge.Id).ToListAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(invitations);
        Assert.Equal("Roster Name", judge.DisplayName);
    }
}
