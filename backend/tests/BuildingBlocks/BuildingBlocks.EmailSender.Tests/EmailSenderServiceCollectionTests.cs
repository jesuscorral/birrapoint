using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.EmailSender.Tests;

public sealed class EmailSenderServiceCollectionTests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by the DI container.")]
    private sealed class HostSender : IEmailSender
    {
        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendWithAttachmentsAsync(
            string toEmail, string subject, string htmlBody, IReadOnlyList<EmailAttachment> attachments, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static IConfigurationSection Section(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection(SmtpOptions.DefaultSectionName);

    private static ServiceProvider Build(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void RegistersASingletonMailKitSender()
    {
        using var provider = Build(services => services.AddEmailSender(Section([])));

        var sender = provider.GetRequiredService<IEmailSender>();

        Assert.IsType<MailKitEmailSender>(sender);
        Assert.Same(sender, provider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void BindsEverySettingFromTheGivenSection()
    {
        var section = Section(new()
        {
            ["Smtp:Host"] = "smtp.example.test",
            ["Smtp:Port"] = "587",
            ["Smtp:UseStartTls"] = "true",
            ["Smtp:From"] = "App <app@example.test>",
            ["Smtp:Username"] = "user",
            ["Smtp:Password"] = "secret",
        });
        using var provider = Build(services => services.AddEmailSender(section));

        var options = provider.GetRequiredService<IOptions<SmtpOptions>>().Value;

        Assert.Equal("smtp.example.test", options.Host);
        Assert.Equal(587, options.Port);
        Assert.True(options.UseStartTls);
        Assert.Equal("App <app@example.test>", options.From);
        Assert.Equal("user", options.Username);
        Assert.Equal("secret", options.Password);
    }

    [Fact]
    public void HostDefaultsApplyWhenTheSectionOmitsTheSetting()
    {
        using var provider = Build(services => services.AddEmailSender(
            Section(new() { ["Smtp:Host"] = "localhost", ["Smtp:Port"] = "1025" }),
            smtp => smtp.From = "Host <default@example.test>"));

        Assert.Equal("Host <default@example.test>", provider.GetRequiredService<IOptions<SmtpOptions>>().Value.From);
    }

    [Fact]
    public void ConfigurationOverridesHostDefaults()
    {
        using var provider = Build(services => services.AddEmailSender(
            Section(new() { ["Smtp:From"] = "Configured <configured@example.test>" }),
            smtp => smtp.From = "Host <default@example.test>"));

        Assert.Equal("Configured <configured@example.test>", provider.GetRequiredService<IOptions<SmtpOptions>>().Value.From);
    }

    [Fact]
    public void KeepsAnEmailSenderTheHostAlreadyRegistered()
    {
        using var provider = Build(services =>
        {
            services.AddSingleton<IEmailSender, HostSender>();
            services.AddEmailSender(Section([]));
        });

        Assert.IsType<HostSender>(provider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void NullArgumentsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddEmailSender(Section([])));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddEmailSender(null!));
    }
}
