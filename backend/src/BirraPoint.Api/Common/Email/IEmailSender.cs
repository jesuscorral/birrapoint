namespace BirraPoint.Api.Common.Email;

/// <summary>Outbound app-side email (R-10) — invitation/result delivery, tracked per-recipient by the caller.</summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken);

    Task SendWithAttachmentsAsync(
        string toEmail, string subject, string htmlBody,
        IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken);
}

/// <summary>An attachment for <see cref="IEmailSender.SendWithAttachmentsAsync"/> (T075 — result PDFs).</summary>
public sealed record EmailAttachment(string FileName, byte[] Content, string ContentType);
