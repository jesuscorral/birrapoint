using System.Collections.Concurrent;
using BirraPoint.Api.Common.Email;

namespace BirraPoint.Api.IntegrationTests.TestHost;

/// <summary>
/// T039 test double — records sent emails in-memory instead of dispatching through MailKit/SMTP.
/// Registered as a singleton (see <see cref="ApiFactory"/>) so tests can inspect deliveries even
/// though the sender runs inside DispatchWorker's own background DI scope, not the request scope.
/// </summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<(string ToEmail, string Subject, string HtmlBody)> _sent = new();

    public IReadOnlyCollection<(string ToEmail, string Subject, string HtmlBody)> Sent => _sent.ToArray();

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        _sent.Enqueue((toEmail, subject, htmlBody));
        return Task.CompletedTask;
    }
}
