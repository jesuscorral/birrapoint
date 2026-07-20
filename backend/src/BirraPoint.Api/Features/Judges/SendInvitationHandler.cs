using System.Net;
using System.Text.Json;
using BirraPoint.Api.Common.Email;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Keycloak;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Judges;

/// <summary>Payload shape enqueued by RegisterJudges/ResendInvitation — no secrets (the temp password is generated fresh inside HandleAsync).</summary>
public sealed record SendInvitationPayload(Guid JudgeId);

/// <summary>
/// First real <see cref="IDispatchJobHandler"/> (T041). Tracks outcome on the <see
/// cref="Invitation"/> row independently of <see cref="DispatchJob"/>'s own Attempts/LastError —
/// on failure it rethrows so <see cref="DispatchWorker"/>'s existing retry/backoff
/// (<see cref="DispatchRetryPolicy"/>) schedules the reattempt; this handler never retries on its own.
/// </summary>
public sealed class SendInvitationHandler(
    AppDbContext dbContext, IKeycloakAdminClient keycloakAdminClient, IEmailSender emailSender, IConfiguration configuration)
    : IDispatchJobHandler
{
    private const int LastErrorMaxLength = 2000; // matches InvitationConfiguration.LastError

    public DispatchJobType Type => DispatchJobType.SendInvitation;

    public async Task HandleAsync(DispatchJob job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SendInvitationPayload>(job.PayloadJson)
            ?? throw new InvalidOperationException($"DispatchJob {job.Id} has an empty SendInvitation payload.");

        var judge = await dbContext.Judges.FirstOrDefaultAsync(j => j.Id == payload.JudgeId, cancellationToken)
            ?? throw new InvalidOperationException($"Judge {payload.JudgeId} not found for DispatchJob {job.Id}.");

        var competition = await dbContext.Competitions.FirstAsync(c => c.Id == judge.CompetitionId, cancellationToken);
        var invitation = await dbContext.Invitations.FirstAsync(i => i.JudgeId == judge.Id, cancellationToken);

        try
        {
            var temporaryPassword = await keycloakAdminClient.EnsureUserWithTemporaryPasswordAsync(judge.Email, cancellationToken);
            var loginUrl = configuration["Frontend:BaseUrl"] ?? throw new InvalidOperationException("Frontend:BaseUrl is not configured.");
            var (subject, body) = BuildInvitationEmail(competition.Name, temporaryPassword, loginUrl);

            await emailSender.SendAsync(judge.Email, subject, body, cancellationToken);

            invitation.Status = InvitationStatus.Sent;
            invitation.SentAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            invitation.Attempts++;
            invitation.LastError = ex.Message.Length > LastErrorMaxLength ? ex.Message[..LastErrorMaxLength] : ex.Message;
            invitation.Status = InvitationStatus.Failed;
            await dbContext.SaveChangesAsync(CancellationToken.None);

            throw;
        }
    }

    private static (string Subject, string Body) BuildInvitationEmail(string competitionName, string temporaryPassword, string loginUrl)
    {
        var subject = $"You're invited to judge {competitionName} on BirraPoint";
        var body = $"""
            <p>You have been invited to judge <strong>{WebUtility.HtmlEncode(competitionName)}</strong> on BirraPoint.</p>
            <p>Your temporary password is: <strong>{WebUtility.HtmlEncode(temporaryPassword)}</strong></p>
            <p>You will be asked to set a new password the first time you log in.</p>
            <p>Log in at <a href="{loginUrl}">{WebUtility.HtmlEncode(loginUrl)}</a>.</p>
            """;
        return (subject, body);
    }
}
