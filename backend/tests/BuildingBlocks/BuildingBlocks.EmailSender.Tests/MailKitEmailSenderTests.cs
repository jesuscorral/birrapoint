using System.Text;
using MailKit.Security;

namespace BuildingBlocks.EmailSender.Tests;

/// <summary>End-to-end delivery through a real SMTP server (Mailpit) without authentication.</summary>
public sealed class MailKitEmailSenderTests(OpenMailpitFixture mailpit) : IClassFixture<OpenMailpitFixture>
{
    [Fact]
    public async Task SendAsyncDeliversAnHtmlEmailFromTheConfiguredSender()
    {
        var recipient = MailpitFixture.NewRecipient();
        var sender = MailpitFixture.Sender(mailpit.Options());

        await sender.SendAsync(recipient, "Invitation", "<p>Hello <b>judge</b></p>", TestContext.Current.CancellationToken);

        var message = await mailpit.SingleMessageToAsync(recipient);
        Assert.Equal("Invitation", message.GetProperty("Subject").GetString());
        Assert.Equal("no-reply@example.test", message.GetProperty("From").GetProperty("Address").GetString());
        Assert.Equal("Block Tests", message.GetProperty("From").GetProperty("Name").GetString());
        Assert.Equal(recipient, Assert.Single(message.GetProperty("To").EnumerateArray()).GetProperty("Address").GetString());
        Assert.Contains("<p>Hello <b>judge</b></p>", message.GetProperty("HTML").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, message.GetProperty("Attachments").GetArrayLength());
    }

    [Fact]
    public async Task SendWithAttachmentsDeliversEveryAttachmentWithItsNameTypeAndBytes()
    {
        // Binary types travel base64-encoded, so their bytes must arrive unchanged (text/* parts get CRLF line endings).
        var recipient = MailpitFixture.NewRecipient();
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7 fake score sheet");
        byte[] zip = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x0A, 0x0D, 0xFF, 0x00];
        var sender = MailpitFixture.Sender(mailpit.Options());

        await sender.SendWithAttachmentsAsync(
            recipient, "Results", "<p>Attached</p>",
            [new EmailAttachment("sheet.pdf", pdf, "application/pdf"), new EmailAttachment("sheets.zip", zip, "application/zip")],
            TestContext.Current.CancellationToken);

        var message = await mailpit.SingleMessageToAsync(recipient);
        var id = message.GetProperty("ID").GetString()!;
        var attachments = message.GetProperty("Attachments").EnumerateArray().ToList();
        Assert.Equal(["sheet.pdf", "sheets.zip"], attachments.Select(a => a.GetProperty("FileName").GetString()));
        Assert.Equal(["application/pdf", "application/zip"], attachments.Select(a => a.GetProperty("ContentType").GetString()));
        Assert.Equal(pdf, await mailpit.AttachmentBytesAsync(id, attachments[0].GetProperty("PartID").GetString()!));
        Assert.Equal(zip, await mailpit.AttachmentBytesAsync(id, attachments[1].GetProperty("PartID").GetString()!));
    }

    [Fact]
    public async Task SendWithAnEmptyAttachmentListSendsAPlainHtmlEmail()
    {
        var recipient = MailpitFixture.NewRecipient();
        var sender = MailpitFixture.Sender(mailpit.Options());

        await sender.SendWithAttachmentsAsync(recipient, "No files", "<p>None</p>", [], TestContext.Current.CancellationToken);

        var message = await mailpit.SingleMessageToAsync(recipient);
        Assert.Equal(0, message.GetProperty("Attachments").GetArrayLength());
    }

    [Fact]
    public async Task EachSendReadsTheCurrentOptionsSoAReloadAppliesWithoutARestart()
    {
        var first = MailpitFixture.NewRecipient();
        var second = MailpitFixture.NewRecipient();
        var monitor = new StaticOptionsMonitor(mailpit.Options());
        var sender = new MailKitEmailSender(monitor);

        await sender.SendAsync(first, "Before", "<p>1</p>", TestContext.Current.CancellationToken);
        monitor.CurrentValue = mailpit.Options(o => o.From = "Reloaded <reloaded@example.test>");
        await sender.SendAsync(second, "After", "<p>2</p>", TestContext.Current.CancellationToken);

        Assert.Equal("no-reply@example.test", (await mailpit.SingleMessageToAsync(first)).GetProperty("From").GetProperty("Address").GetString());
        Assert.Equal("reloaded@example.test", (await mailpit.SingleMessageToAsync(second)).GetProperty("From").GetProperty("Address").GetString());
    }

    [Fact]
    public async Task StartTlsIsRequestedWhenConfigured()
    {
        // Mailpit here offers no STARTTLS, so MailKit refusing to continue proves the option reaches the connection.
        var recipient = MailpitFixture.NewRecipient();
        var sender = MailpitFixture.Sender(mailpit.Options(o => o.UseStartTls = true));

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            sender.SendAsync(recipient, "Secure", "<p>x</p>", TestContext.Current.CancellationToken));
        Assert.Equal(0, await mailpit.CountMessagesToAsync(recipient));
    }

    [Fact]
    public async Task InvalidConfigurationFailsBeforeConnecting()
    {
        var sender = MailpitFixture.Sender(new SmtpOptions { Host = "unreachable.invalid", Port = 25 });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync(MailpitFixture.NewRecipient(), "x", "<p>x</p>", TestContext.Current.CancellationToken));
        Assert.Contains("From", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACancelledTokenStopsTheSend()
    {
        var recipient = MailpitFixture.NewRecipient();
        var sender = MailpitFixture.Sender(mailpit.Options());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sender.SendAsync(recipient, "x", "<p>x</p>", new CancellationToken(canceled: true)));
        Assert.Equal(0, await mailpit.CountMessagesToAsync(recipient));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task BlankRecipientIsRejected(string? recipient)
    {
        var sender = MailpitFixture.Sender(mailpit.Options());

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            sender.SendAsync(recipient!, "x", "<p>x</p>", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NullSubjectBodyOrAttachmentsAreRejected()
    {
        var sender = MailpitFixture.Sender(mailpit.Options());
        var recipient = MailpitFixture.NewRecipient();
        var token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(() => sender.SendAsync(recipient, null!, "<p>x</p>", token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sender.SendAsync(recipient, "x", null!, token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sender.SendWithAttachmentsAsync(recipient, "x", "<p>x</p>", null!, token));
    }

    [Fact]
    public void BuildMessageSetsHeadersHtmlBodyAndAttachments()
    {
        using var message = MailKitEmailSender.BuildMessage(
            "Sender <from@example.test>", "to@example.test", "Subject", "<p>Body</p>",
            [new EmailAttachment("a.txt", "abc"u8.ToArray(), "text/plain")]);

        Assert.Equal("from@example.test", Assert.Single(message.From.Mailboxes).Address);
        Assert.Equal("to@example.test", Assert.Single(message.To.Mailboxes).Address);
        Assert.Equal("Subject", message.Subject);
        Assert.Equal("<p>Body</p>", message.HtmlBody);
        var attachment = Assert.IsAssignableFrom<MimeKit.MimePart>(Assert.Single(message.Attachments));
        Assert.Equal("a.txt", attachment.FileName);
        Assert.Equal("text/plain", attachment.ContentType.MimeType);
    }
}

/// <summary>Delivery through an SMTP relay that requires authentication.</summary>
public sealed class AuthenticatedMailKitEmailSenderTests(AuthenticatedMailpitFixture mailpit) : IClassFixture<AuthenticatedMailpitFixture>
{
    [Fact]
    public async Task ConfiguredCredentialsAuthenticateAndDeliver()
    {
        var recipient = MailpitFixture.NewRecipient();
        var sender = MailpitFixture.Sender(mailpit.Options(o =>
        {
            o.Username = AuthenticatedMailpitFixture.Username;
            o.Password = AuthenticatedMailpitFixture.Password;
        }));

        await sender.SendAsync(recipient, "Authenticated", "<p>ok</p>", TestContext.Current.CancellationToken);

        Assert.Equal("Authenticated", (await mailpit.SingleMessageToAsync(recipient)).GetProperty("Subject").GetString());
    }

    [Fact]
    public async Task WrongPasswordIsRejectedByTheServer()
    {
        var recipient = MailpitFixture.NewRecipient();
        var sender = MailpitFixture.Sender(mailpit.Options(o =>
        {
            o.Username = AuthenticatedMailpitFixture.Username;
            o.Password = "wrong";
        }));

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            sender.SendAsync(recipient, "x", "<p>x</p>", TestContext.Current.CancellationToken));
        Assert.Equal(0, await mailpit.CountMessagesToAsync(recipient));
    }

    [Fact]
    public async Task WithoutAUsernameTheSenderDoesNotAuthenticate()
    {
        // A relay that requires authentication refuses the message: proves no credentials are sent.
        var recipient = MailpitFixture.NewRecipient();
        var sender = MailpitFixture.Sender(mailpit.Options());

        await Assert.ThrowsAnyAsync<Exception>(() =>
            sender.SendAsync(recipient, "x", "<p>x</p>", TestContext.Current.CancellationToken));
        Assert.Equal(0, await mailpit.CountMessagesToAsync(recipient));
    }
}
