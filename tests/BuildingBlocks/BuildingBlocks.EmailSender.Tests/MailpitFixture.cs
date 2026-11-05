using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.EmailSender.Tests;

/// <summary>
/// A real Mailpit SMTP sink in a container (the same server BirraPoint uses locally), read back through
/// its REST API. Each test sends to its own recipient, so tests share the container without interfering.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires fixtures used in public test-class constructors (IClassFixture<T>) to be public.")]
public abstract class MailpitFixture : IAsyncLifetime
{
    private const ushort SmtpPort = 1025;
    private const ushort HttpPort = 8025;

    private readonly IContainer _container;
    private HttpClient _api = null!;

    protected MailpitFixture(IReadOnlyDictionary<string, string> environment)
    {
        _container = new ContainerBuilder("axllent/mailpit:v1.22.3")
            .WithPortBinding(SmtpPort, assignRandomHostPort: true)
            .WithPortBinding(HttpPort, assignRandomHostPort: true)
            .WithEnvironment(environment)
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilInternalTcpPortIsAvailable(SmtpPort)
                .UntilHttpRequestIsSucceeded(request => request.ForPort(HttpPort).ForPath("/api/v1/info")))
            .Build();
    }

    internal string Host => _container.Hostname;

    internal int Port => _container.GetMappedPublicPort(SmtpPort);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _api = new HttpClient
        {
            BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpPort)}"),
        };
    }

    public async ValueTask DisposeAsync()
    {
        _api.Dispose();
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    internal SmtpOptions Options(Action<SmtpOptions>? change = null)
    {
        var options = new SmtpOptions { Host = Host, Port = Port, From = "Block Tests <no-reply@example.test>" };
        change?.Invoke(options);
        return options;
    }

    internal static MailKitEmailSender Sender(SmtpOptions options) => new(new StaticOptionsMonitor(options));

    /// <summary>The single message delivered to <paramref name="recipient"/>, polling briefly for it to land.</summary>
    internal async Task<JsonElement> SingleMessageToAsync(string recipient)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var search = await _api.GetFromJsonAsync<JsonElement>(
                new Uri($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{recipient}\"")}", UriKind.Relative), cancellationToken);
            var messages = search.GetProperty("messages");
            if (messages.GetArrayLength() > 0)
            {
                var id = Assert.Single(messages.EnumerateArray()).GetProperty("ID").GetString();
                return await _api.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/message/{id}", UriKind.Relative), cancellationToken);
            }

            await Task.Delay(100, cancellationToken);
        }

        Assert.Fail($"No message reached {recipient}.");
        return default;
    }

    internal async Task<int> CountMessagesToAsync(string recipient)
    {
        var search = await _api.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{recipient}\"")}", UriKind.Relative),
            TestContext.Current.CancellationToken);
        return search.GetProperty("messages").GetArrayLength();
    }

    internal Task<byte[]> AttachmentBytesAsync(string messageId, string partId) =>
        _api.GetByteArrayAsync(new Uri($"/api/v1/message/{messageId}/part/{partId}", UriKind.Relative), TestContext.Current.CancellationToken);

    internal static string NewRecipient() => $"to-{Guid.NewGuid():N}@example.test";
}

/// <summary>Mailpit accepting any sender without authentication (BirraPoint's local setup).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires fixtures used in public test-class constructors (IClassFixture<T>) to be public.")]
public sealed class OpenMailpitFixture() : MailpitFixture(new Dictionary<string, string>(StringComparer.Ordinal));

/// <summary>Mailpit requiring SMTP authentication with fixed credentials (a production-like relay, without TLS).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "xUnit requires fixtures used in public test-class constructors (IClassFixture<T>) to be public.")]
public sealed class AuthenticatedMailpitFixture() : MailpitFixture(new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["MP_SMTP_AUTH"] = $"{AuthenticatedMailpitFixture.Username}:{AuthenticatedMailpitFixture.Password}",
    ["MP_SMTP_AUTH_ALLOW_INSECURE"] = "true",
})
{
    internal const string Username = "relay-user";
    internal const string Password = "relay-password";
}

/// <summary>An <see cref="IOptionsMonitor{TOptions}"/> whose value the test can swap to simulate a configuration reload.</summary>
internal sealed class StaticOptionsMonitor(SmtpOptions options) : IOptionsMonitor<SmtpOptions>
{
    public SmtpOptions CurrentValue { get; set; } = options;

    public SmtpOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<SmtpOptions, string?> listener) => null;
}
