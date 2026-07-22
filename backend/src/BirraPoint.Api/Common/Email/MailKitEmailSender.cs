using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace BirraPoint.Api.Common.Email;

public sealed class MailKitEmailSender(IConfiguration configuration) : IEmailSender
{
    private const string FromAddress = "BirraPoint <no-reply@birrapoint.local>";

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken) =>
        SendMessageAsync(BuildMessage(toEmail, subject, htmlBody, attachments: null), cancellationToken);

    public Task SendWithAttachmentsAsync(
        string toEmail, string subject, string htmlBody,
        IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken) =>
        SendMessageAsync(BuildMessage(toEmail, subject, htmlBody, attachments), cancellationToken);

    private static MimeMessage BuildMessage(
        string toEmail, string subject, string htmlBody, IReadOnlyList<EmailAttachment>? attachments)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
        foreach (var attachment in attachments ?? [])
        {
            bodyBuilder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        message.Body = bodyBuilder.ToMessageBody();
        return message;
    }

    private async Task SendMessageAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        var host = configuration["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host is not configured.");
        var port = int.Parse(configuration["Smtp:Port"] ?? throw new InvalidOperationException("Smtp:Port is not configured."));

        using var client = new SmtpClient();
        // Mailpit locally needs no TLS/auth; production SMTP relay is a Phase 16 deployment decision.
        await client.ConnectAsync(host, port, SecureSocketOptions.None, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
