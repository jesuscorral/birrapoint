using System.Runtime.InteropServices;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BuildingBlocks.EmailSender;

/// <summary>
/// <see cref="IEmailSender"/> over SMTP with MailKit: one connection per email. Options are read on every
/// send through <see cref="IOptionsMonitor{TOptions}"/>, so configuration reloads apply without a restart.
/// </summary>
internal sealed class MailKitEmailSender(IOptionsMonitor<SmtpOptions> options) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken) =>
        SendMessageAsync(toEmail, subject, htmlBody, [], cancellationToken);

    public Task SendWithAttachmentsAsync(
        string toEmail, string subject, string htmlBody,
        IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        return SendMessageAsync(toEmail, subject, htmlBody, attachments, cancellationToken);
    }

    internal static MimeMessage BuildMessage(
        string fromAddress, string toEmail, string subject, string htmlBody, IReadOnlyList<EmailAttachment> attachments)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
        foreach (var attachment in attachments)
        {
            // MimeKit copies the stream into the message, so the stream can be disposed right after.
            using var content = OpenRead(attachment.Content);
            bodyBuilder.Attachments.Add(attachment.FileName, content, ContentType.Parse(attachment.ContentType));
        }

        message.Body = bodyBuilder.ToMessageBody();
        return message;
    }

    /// <summary>Reads array-backed memory in place; only memory owned elsewhere (pooled, native) is copied.</summary>
    private static MemoryStream OpenRead(ReadOnlyMemory<byte> content) =>
        MemoryMarshal.TryGetArray(content, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(content.ToArray(), writable: false);

    private async Task SendMessageAsync(
        string toEmail, string subject, string htmlBody, IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toEmail);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(htmlBody);

        var settings = SmtpSettings.Resolve(options.CurrentValue);
        using var message = BuildMessage(settings.From, toEmail, subject, htmlBody, attachments);

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host, settings.Port, settings.SecureSocketOptions, cancellationToken).ConfigureAwait(false);
        if (settings.RequiresAuthentication)
        {
            // RequiresAuthentication guarantees both are non-null (SmtpSettings.Resolve). Never log the password.
            await client.AuthenticateAsync(settings.Username!, settings.Password!, cancellationToken).ConfigureAwait(false);
        }

        await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
    }
}
