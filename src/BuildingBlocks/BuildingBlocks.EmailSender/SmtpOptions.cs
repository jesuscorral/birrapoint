namespace BuildingBlocks.EmailSender;

/// <summary>
/// SMTP connection and sender settings, usually bound from the <see cref="DefaultSectionName"/>
/// configuration section. They are validated on every send, not at startup, so a host without SMTP
/// configured still starts and fails only when it tries to send.
/// </summary>
public sealed class SmtpOptions
{
    /// <summary>Conventional configuration section name (<c>Smtp:Host</c>, <c>Smtp:Port</c>, ...).</summary>
    public const string DefaultSectionName = "Smtp";

    /// <summary>SMTP server host name. Required.</summary>
    public string? Host { get; set; }

    /// <summary>SMTP server port (1-65535). Required.</summary>
    public int Port { get; set; }

    /// <summary>Upgrade the connection with STARTTLS. Off by default (e.g. a local Mailpit sink).</summary>
    public bool UseStartTls { get; set; }

    /// <summary>Sender mailbox, e.g. <c>My App &lt;no-reply@example.com&gt;</c>. Required.</summary>
    public string? From { get; set; }

    /// <summary>User name; when set, the sender authenticates and <see cref="Password"/> is required.</summary>
    public string? Username { get; set; }

    /// <summary>Password for <see cref="Username"/>. Never logged.</summary>
    public string? Password { get; set; }
}
