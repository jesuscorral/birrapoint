using BirraPoint.Api.Common.Email;
using MailKit.Security;
using Microsoft.Extensions.Configuration;

namespace BirraPoint.Api.UnitTests.Common.Email;

/// <summary>
/// T095-T097: <see cref="SmtpSettings.Resolve"/> is the pure decision layer extracted out of
/// <see cref="MailKitEmailSender"/> — socket security, sender address, and whether to
/// authenticate — so those decisions are unit-testable without a real SMTP server. Local Mailpit
/// only ever supplies Smtp:Host/Smtp:Port (as configured by the AppHost), so that minimal shape
/// must keep resolving to the pre-T095 behavior: no TLS, no auth, default From address.
/// </summary>
public sealed class SmtpSettingsTests
{
    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Mailpit_shaped_config_resolves_to_no_tls_and_no_authentication()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "1025",
        });

        var settings = SmtpSettings.Resolve(configuration);

        Assert.Equal("localhost", settings.Host);
        Assert.Equal(1025, settings.Port);
        Assert.Equal(SecureSocketOptions.None, settings.SecureSocketOptions);
        Assert.False(settings.RequiresAuthentication);
        Assert.Null(settings.Username);
        Assert.Null(settings.Password);
        Assert.Equal("BirraPoint <no-reply@birrapoint.local>", settings.From);
    }

    [Fact]
    public void UseStartTls_true_resolves_to_StartTls_socket_option()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.relay.example",
            ["Smtp:Port"] = "587",
            ["Smtp:UseStartTls"] = "true",
        });

        var settings = SmtpSettings.Resolve(configuration);

        Assert.Equal(SecureSocketOptions.StartTls, settings.SecureSocketOptions);
    }

    [Fact]
    public void UseStartTls_false_or_absent_resolves_to_no_socket_security()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.relay.example",
            ["Smtp:Port"] = "587",
            ["Smtp:UseStartTls"] = "false",
        });

        var settings = SmtpSettings.Resolve(configuration);

        Assert.Equal(SecureSocketOptions.None, settings.SecureSocketOptions);
    }

    [Fact]
    public void Non_empty_username_requires_authentication_and_carries_the_password_through()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.relay.example",
            ["Smtp:Port"] = "587",
            ["Smtp:UseStartTls"] = "true",
            ["Smtp:Username"] = "birrapoint-relay",
            ["Smtp:Password"] = "super-secret",
        });

        var settings = SmtpSettings.Resolve(configuration);

        Assert.True(settings.RequiresAuthentication);
        Assert.Equal("birrapoint-relay", settings.Username);
        Assert.Equal("super-secret", settings.Password);
    }

    [Fact]
    public void Empty_username_does_not_require_authentication_even_if_present_as_a_blank_value()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "1025",
            ["Smtp:Username"] = "",
        });

        var settings = SmtpSettings.Resolve(configuration);

        Assert.False(settings.RequiresAuthentication);
    }

    [Fact]
    public void Custom_from_address_overrides_the_default()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.relay.example",
            ["Smtp:Port"] = "587",
            ["Smtp:From"] = "BirraPoint <notificaciones@birrapoint.app>",
        });

        var settings = SmtpSettings.Resolve(configuration);

        Assert.Equal("BirraPoint <notificaciones@birrapoint.app>", settings.From);
    }

    [Fact]
    public void Missing_host_throws()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Smtp:Port"] = "1025",
        });

        Assert.Throws<InvalidOperationException>(() => SmtpSettings.Resolve(configuration));
    }

    [Fact]
    public void Missing_port_throws()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "localhost",
        });

        Assert.Throws<InvalidOperationException>(() => SmtpSettings.Resolve(configuration));
    }

    [Fact]
    public void Username_without_a_password_throws()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.relay.example",
            ["Smtp:Port"] = "587",
            ["Smtp:Username"] = "birrapoint-relay",
        });

        Assert.Throws<InvalidOperationException>(() => SmtpSettings.Resolve(configuration));
    }
}
