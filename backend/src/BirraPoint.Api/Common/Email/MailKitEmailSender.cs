using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace BirraPoint.Api.Common.Email;

public sealed class MailKitEmailSender(IConfiguration configuration) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken) =>
        SendMessageAsync(toEmail, subject, htmlBody, attachments: null, cancellationToken);

    public Task SendWithAttachmentsAsync(
        string toEmail, string subject, string htmlBody,
        IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken) =>
        SendMessageAsync(toEmail, subject, htmlBody, attachments, cancellationToken);

    private static MimeMessage BuildMessage(
        string fromAddress, string toEmail, string subject, string htmlBody, IReadOnlyList<EmailAttachment>? attachments)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(fromAddress));
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

    private async Task SendMessageAsync(
        string toEmail, string subject, string htmlBody, IReadOnlyList<EmailAttachment>? attachments, CancellationToken cancellationToken)
    {
        var settings = SmtpSettings.Resolve(configuration);
        var message = BuildMessage(settings.From, toEmail, subject, htmlBody, attachments);

        using var client = new SmtpClient();
        // Mailpit locally needs no TLS/auth (SmtpSettings resolves that shape unchanged); a
        // production relay additionally supplies UseStartTls + Username/Password.
        await client.ConnectAsync(settings.Host, settings.Port, settings.SecureSocketOptions, cancellationToken);
        if (settings.RequiresAuthentication)
        {
            // RequiresAuthentication guarantees both are non-null (SmtpSettings.Resolve). Never log settings.Password.
            await client.AuthenticateAsync(settings.Username!, settings.Password!, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
