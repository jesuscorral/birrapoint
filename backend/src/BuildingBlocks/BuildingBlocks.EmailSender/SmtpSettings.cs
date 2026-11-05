using MailKit.Security;
using MimeKit;

namespace BuildingBlocks.EmailSender;

/// <summary>
/// Validated SMTP settings resolved from <see cref="SmtpOptions"/>: socket security, sender address and
/// whether to authenticate. Kept apart from <see cref="MailKitEmailSender"/>'s I/O so these decisions are
/// testable without a server. A host/port-only configuration (a local Mailpit sink) resolves to no TLS
/// and no authentication.
/// </summary>
internal sealed record SmtpSettings(
    string Host,
    int Port,
    SecureSocketOptions SecureSocketOptions,
    string From,
    string? Username,
    string? Password)
{
    private const int MaxPort = 65535;

    /// <summary>Authenticate only when a non-empty user name is configured.</summary>
    public bool RequiresAuthentication => !string.IsNullOrEmpty(Username);

    /// <summary>Validates <paramref name="options"/>; throws <see cref="InvalidOperationException"/> naming the missing or invalid setting.</summary>
    public static SmtpSettings Resolve(SmtpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            throw new InvalidOperationException("SMTP Host is not configured.");
        }

        if (options.Port is < 1 or > MaxPort)
        {
            throw new InvalidOperationException($"SMTP Port must be between 1 and {MaxPort}; it is {options.Port}.");
        }

        if (string.IsNullOrWhiteSpace(options.From))
        {
            throw new InvalidOperationException("SMTP From is not configured.");
        }

        if (!MailboxAddress.TryParse(options.From, out _))
        {
            throw new InvalidOperationException($"SMTP From '{options.From}' is not a valid mailbox address.");
        }

        var authenticate = !string.IsNullOrEmpty(options.Username);
        if (authenticate && string.IsNullOrEmpty(options.Password))
        {
            throw new InvalidOperationException("SMTP Password is not configured but Username is set.");
        }

        return new SmtpSettings(
            Host: options.Host,
            Port: options.Port,
            SecureSocketOptions: options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            From: options.From,
            Username: authenticate ? options.Username : null,
            Password: authenticate ? options.Password : null);
    }
}
