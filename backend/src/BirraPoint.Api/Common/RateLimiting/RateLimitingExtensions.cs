using System.Globalization;
using System.Threading.RateLimiting;
using BirraPoint.Api.Common.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BirraPoint.Api.Common.RateLimiting;

/// <summary>Bound from <c>RateLimiting:NotifyJudges</c> (T147/FR-065).</summary>
internal sealed class NotifyJudgesRateLimitOptions
{
    public const string SectionName = "RateLimiting:NotifyJudges";

    public int PermitLimit { get; set; } = RateLimitingExtensions.DefaultPermitLimit;

    public int WindowSeconds { get; set; } = (int)RateLimitingExtensions.DefaultWindow.TotalSeconds;
}

internal static class RateLimitingExtensions
{
    public const string NotifyJudgesPolicy = "NotifyJudges";
    public const int DefaultPermitLimit = 2;
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(30);

    public static IServiceCollection ConfigureRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<NotifyJudgesRateLimitOptions>()
            .BindConfiguration(NotifyJudgesRateLimitOptions.SectionName)
            .Validate(o => o.PermitLimit > 0 && o.WindowSeconds > 0,
                "RateLimiting:NotifyJudges PermitLimit and WindowSeconds must be positive.");

        // NotifyJudges is the only rate-limited endpoint. The default policy is a no-op.
        // The window is partitioned per organizer (JWT `sub`) so one organizer can never
        // throttle another. The endpoint requires ORGANIZER, so `sub` is always present there;
        // a request without one is not throttled (it is rejected by authorization anyway).
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRateLimitedProblemAsync;

            options.AddPolicy(NotifyJudgesPolicy, context =>
            {
                var sub = context.User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(sub))
                {
                    return RateLimitPartition.GetNoLimiter(string.Empty);
                }

                var settings = context.RequestServices.GetRequiredService<IOptions<NotifyJudgesRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    sub,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }

    private static async ValueTask WriteRateLimitedProblemAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var (urn, status, title) = DomainErrorCatalog.Entries[DomainErrorType.RateLimited];
        var response = context.HttpContext.Response;
        response.StatusCode = status;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
        }

        var problem = new ProblemDetails
        {
            Type = urn,
            Title = title,
            Status = status,
            Detail = "Too many requests. Wait before retrying.",
        };

        await response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
    }
}
