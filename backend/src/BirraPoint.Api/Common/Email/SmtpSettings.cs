using MailKit.Security;

namespace BirraPoint.Api.Common.Email;

/// <summary>
/// T095-T097: pure resolution of SMTP connection settings from configuration — socket security,
/// sender address, and whether to authenticate — kept separate from <see cref="MailKitEmailSender"/>'s
/// I/O so these decisions are unit-testable without a real SMTP server. Local Mailpit only ever
/// supplies Smtp:Host/Smtp:Port, which must keep resolving to no TLS / no auth (pre-T095 behavior);
/// a production relay additionally supplies Smtp:UseStartTls/Smtp:Username/Smtp:Password.
/// </summary>
public sealed record SmtpSettings(
    string Host,
    int Port,
    SecureSocketOptions SecureSocketOptions,
    string From,
    string? Username,
    string? Password)
{
    public const string DefaultFrom = "BirraPoint <no-reply@birrapoint.local>";

    /// <summary>Authenticate only when a non-empty username is configured — never log the password.</summary>
    public bool RequiresAuthentication => !string.IsNullOrEmpty(Username);

    public static SmtpSettings Resolve(IConfiguration configuration)
    {
        var host = configuration["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host is not configured.");
        var port = int.Parse(configuration["Smtp:Port"] ?? throw new InvalidOperationException("Smtp:Port is not configured."));
        var useStartTls = configuration.GetValue("Smtp:UseStartTls", false);
        var username = configuration["Smtp:Username"];
        var password = configuration["Smtp:Password"];

        if (!string.IsNullOrEmpty(username) && string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Smtp:Password is not configured but Smtp:Username is set.");
        }

        return new SmtpSettings(
            Host: host,
            Port: port,
            SecureSocketOptions: useStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            From: configuration["Smtp:From"] ?? DefaultFrom,
            Username: string.IsNullOrEmpty(username) ? null : username,
            Password: string.IsNullOrEmpty(username) ? null : password);
    }
}
