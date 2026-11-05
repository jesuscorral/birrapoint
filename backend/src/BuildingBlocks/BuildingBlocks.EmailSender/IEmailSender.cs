namespace BuildingBlocks.EmailSender;

/// <summary>Sends one HTML email to one recipient. Delivery tracking, retries and templating belong to the caller.</summary>
public interface IEmailSender
{
    /// <summary>Sends an HTML email without attachments.</summary>
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken);

    /// <summary>Sends an HTML email with the given attachments (an empty list sends none).</summary>
    Task SendWithAttachmentsAsync(
        string toEmail, string subject, string htmlBody,
        IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken);
}

/// <summary>A file attached to an email sent through <see cref="IEmailSender.SendWithAttachmentsAsync"/>.</summary>
/// <param name="FileName">Name shown to the recipient, e.g. <c>report.pdf</c>.</param>
/// <param name="Content">Raw file bytes.</param>
/// <param name="ContentType">MIME type, e.g. <c>application/pdf</c>.</param>
public sealed record EmailAttachment(string FileName, ReadOnlyMemory<byte> Content, string ContentType);
