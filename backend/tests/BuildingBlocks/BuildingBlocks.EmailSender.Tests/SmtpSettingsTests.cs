using MailKit.Security;

namespace BuildingBlocks.EmailSender.Tests;

/// <summary>
/// <see cref="SmtpSettings.Resolve"/> is the pure decision layer of <see cref="MailKitEmailSender"/>: socket
/// security, sender address and whether to authenticate. A host/port-only configuration (a local Mailpit
/// sink) must resolve to no TLS and no authentication.
/// </summary>
public sealed class SmtpSettingsTests
{
    private const string From = "My App <no-reply@example.test>";

    private static SmtpOptions Options(Action<SmtpOptions>? change = null)
    {
        var options = new SmtpOptions { Host = "localhost", Port = 1025, From = From };
        change?.Invoke(options);
        return options;
    }

    [Fact]
    public void HostAndPortOnlyResolvesToNoTlsAndNoAuthentication()
    {
        var settings = SmtpSettings.Resolve(Options());

        Assert.Equal("localhost", settings.Host);
        Assert.Equal(1025, settings.Port);
        Assert.Equal(SecureSocketOptions.None, settings.SecureSocketOptions);
        Assert.Equal(From, settings.From);
        Assert.False(settings.RequiresAuthentication);
        Assert.Null(settings.Username);
        Assert.Null(settings.Password);
    }

    [Fact]
    public void UseStartTlsResolvesToTheStartTlsSocketOption()
    {
        var settings = SmtpSettings.Resolve(Options(o => o.UseStartTls = true));

        Assert.Equal(SecureSocketOptions.StartTls, settings.SecureSocketOptions);
    }

    [Fact]
    public void UsernameRequiresAuthenticationAndCarriesThePasswordThrough()
    {
        var settings = SmtpSettings.Resolve(Options(o =>
        {
            o.Username = "relay-user";
            o.Password = "super-secret";
        }));

        Assert.True(settings.RequiresAuthentication);
        Assert.Equal("relay-user", settings.Username);
        Assert.Equal("super-secret", settings.Password);
    }

    [Fact]
    public void BlankUsernameDoesNotAuthenticateAndDropsAStrayPassword()
    {
        var settings = SmtpSettings.Resolve(Options(o =>
        {
            o.Username = "";
            o.Password = "ignored";
        }));

        Assert.False(settings.RequiresAuthentication);
        Assert.Null(settings.Username);
        Assert.Null(settings.Password);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void PortBoundsAreAccepted(int port)
    {
        Assert.Equal(port, SmtpSettings.Resolve(Options(o => o.Port = port)).Port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingHostThrows(string? host)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SmtpSettings.Resolve(Options(o => o.Host = host)));

        Assert.Contains("Host", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void PortOutsideTheValidRangeThrows(int port)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SmtpSettings.Resolve(Options(o => o.Port = port)));

        Assert.Contains("Port", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void MissingFromThrows(string? from)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SmtpSettings.Resolve(Options(o => o.From = from)));

        Assert.Contains("From", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidFromMailboxThrows()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SmtpSettings.Resolve(Options(o => o.From = "My App <not an address")));

        Assert.Contains("not a valid mailbox", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UsernameWithoutAPasswordThrows(string? password)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SmtpSettings.Resolve(Options(o =>
        {
            o.Username = "relay-user";
            o.Password = password;
        })));

        Assert.Contains("Password", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullOptionsThrows()
    {
        Assert.Throws<ArgumentNullException>(() => SmtpSettings.Resolve(null!));
    }
}
