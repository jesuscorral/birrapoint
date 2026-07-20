using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace BirraPoint.Api.Common.Email;

public sealed class MailKitEmailSender(IConfiguration configuration) : IEmailSender
{
    private const string FromAddress = "BirraPoint <no-reply@birrapoint.local>";

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        var host = configuration["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host is not configured.");
        var port = int.Parse(configuration["Smtp:Port"] ?? throw new InvalidOperationException("Smtp:Port is not configured."));

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(FromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        // Mailpit locally needs no TLS/auth; production SMTP relay is a Phase 16 deployment decision.
        await client.ConnectAsync(host, port, SecureSocketOptions.None, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
