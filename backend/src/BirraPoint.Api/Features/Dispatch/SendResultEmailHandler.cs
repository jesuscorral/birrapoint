using System.Net;
using System.Text.Json;
using BirraPoint.Api.Common.Email;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Dispatch;

/// <summary>Payload shape enqueued by BundleZipHandler.</summary>
public sealed record SendResultEmailPayload(Guid ParticipantId);

/// <summary>
/// T074/FR-041: emails a participant every PDF scoresheet for their own entries. Unlike <see
/// cref="Judges.SendInvitationHandler"/>, there's no separate status entity to update on failure —
/// this job's own <see cref="DispatchJob.Status"/>/Attempts/LastError (surfaced by GET /dispatch)
/// IS the delivery-status record, so a failure simply rethrows for <see cref="DispatchWorker"/>'s
/// existing retry/backoff to handle; this handler never retries on its own.
/// </summary>
public sealed class SendResultEmailHandler(AppDbContext dbContext, IEmailSender emailSender) : IDispatchJobHandler
{
    public DispatchJobType Type => DispatchJobType.SendResultEmail;

    public async Task HandleAsync(DispatchJob job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SendResultEmailPayload>(job.PayloadJson)
            ?? throw new InvalidOperationException($"DispatchJob {job.Id} has an empty SendResultEmail payload.");

        var participant = await dbContext.Participants.FirstAsync(p => p.Id == payload.ParticipantId, cancellationToken);
        var competition = await dbContext.Competitions.FirstAsync(c => c.Id == job.CompetitionId, cancellationToken);

        var sheets = await dbContext.BeerEntries
            .Where(e => e.ParticipantId == payload.ParticipantId)
            .Join(dbContext.GeneratedScoreSheets, e => e.Id, s => s.BeerEntryId,
                (e, s) => new { e.StyleCode, e.BlindCode, s.PdfBytes })
            .ToListAsync(cancellationToken);

        var attachments = sheets
            .Select(s => new EmailAttachment($"{s.StyleCode}_{s.BlindCode}.pdf", s.PdfBytes, "application/pdf"))
            .ToList();

        var (subject, body) = BuildResultEmail(competition.Name);

        await emailSender.SendWithAttachmentsAsync(participant.Email, subject, body, attachments, cancellationToken);
    }

    private static (string Subject, string Body) BuildResultEmail(string competitionName)
    {
        var subject = $"Your BirraPoint results for {competitionName}";
        var body = $"""
            <p>Thank you for entering <strong>{WebUtility.HtmlEncode(competitionName)}</strong>.</p>
            <p>Your evaluation sheets are attached as PDF files.</p>
            """;
        return (subject, body);
    }
}
