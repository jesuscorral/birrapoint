using System.Net;
using System.Text.Json;
using BirraPoint.Api.Common.Jobs;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Modules.Organization.Contracts;
using BirraPoint.Api.Modules.Publication.Features.Dispatch.CommonComponents;
using BuildingBlocks.EmailSender;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.DispatchScoreSheet;

/// <summary>
/// FR-041: emails a participant every PDF scoresheet for their own entries. Unlike <see
/// cref="Judges.SendInvitationHandler"/>, there's no separate status entity to update on failure —
/// this job's own <see cref="DispatchJob.Status"/>/Attempts/LastError (surfaced by GET /dispatch)
/// IS the delivery-status record, so a failure simply rethrows for <see cref="DispatchWorker"/>'s
/// existing retry/backoff to handle; this handler never retries on its own.
/// </summary>
internal sealed class SendResultEmailHandler(
    AppDbContext dbContext, ICompetitionAccess competitionAccess, IResultRecipients resultRecipients, IEmailSender emailSender)
    : IDispatchJobHandler
{
    public DispatchJobType Type => DispatchJobType.SendResultEmail;

    public async Task HandleAsync(DispatchJob job, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SendResultEmailPayload>(job.PayloadJson)
            ?? throw new InvalidOperationException($"DispatchJob {job.Id} has an empty SendResultEmail payload.");

        var recipient = await resultRecipients.GetRecipientAsync(payload.ParticipantId, cancellationToken)
            ?? throw new InvalidOperationException($"Participant {payload.ParticipantId} not found.");
        var competition = await competitionAccess.GetAsync(job.CompetitionId, cancellationToken)
            ?? throw new InvalidOperationException($"Competition {job.CompetitionId} not found.");

        var entries = await resultRecipients.GetParticipantEntriesAsync(payload.ParticipantId, cancellationToken);
        var entryIds = entries.Select(e => e.EntryId).ToList();
        var pdfByEntryId = await dbContext.GeneratedScoreSheets
            .Where(s => entryIds.Contains(s.BeerEntryId))
            .Select(s => new { s.BeerEntryId, s.PdfBytes })
            .ToDictionaryAsync(s => s.BeerEntryId, s => s.PdfBytes, cancellationToken);
        var sheets = entries
            .Where(e => pdfByEntryId.ContainsKey(e.EntryId))
            .Select(e => new { e.StyleCode, e.BlindCode, PdfBytes = pdfByEntryId[e.EntryId] })
            .ToList();

        var attachments = sheets
            .Select(s => new EmailAttachment($"{s.StyleCode}_{s.BlindCode}.pdf", s.PdfBytes, "application/pdf"))
            .ToList();

        var (subject, body) = BuildResultEmail(competition.Name);

        await emailSender.SendWithAttachmentsAsync(recipient.Email, subject, body, attachments, cancellationToken);
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
