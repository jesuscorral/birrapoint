namespace BirraPoint.Api.Common.Email;

/// <summary>Outbound app-side email (R-10) — invitation/result delivery, tracked per-recipient by the caller.</summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken);
}
