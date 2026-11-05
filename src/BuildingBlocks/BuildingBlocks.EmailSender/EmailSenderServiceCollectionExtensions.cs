using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.EmailSender;

/// <summary>Registers the SMTP <see cref="IEmailSender"/>.</summary>
public static class EmailSenderServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="SmtpOptions"/> from <paramref name="configuration"/> (pass the section, e.g.
    /// <c>builder.Configuration.GetSection(SmtpOptions.DefaultSectionName)</c>), then applies
    /// <paramref name="configure"/> for host defaults such as <see cref="SmtpOptions.From"/>. Registers the
    /// MailKit sender as a singleton unless the host already registered an <see cref="IEmailSender"/>.
    /// </summary>
    public static IServiceCollection AddEmailSender(
        this IServiceCollection services, IConfiguration configuration, Action<SmtpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = services.AddOptions<SmtpOptions>();
        if (configure is not null)
        {
            // Defaults first, so values present in configuration win.
            options.Configure(configure);
        }

        options.Bind(configuration);
        services.TryAddSingleton<IEmailSender, MailKitEmailSender>();
        return services;
    }
}
