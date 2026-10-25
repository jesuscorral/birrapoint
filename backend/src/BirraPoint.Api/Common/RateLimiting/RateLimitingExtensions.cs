using System.Threading.RateLimiting;

namespace BirraPoint.Api.Common.RateLimiting;

internal static class RateLimitingExtensions
{
    public const string NotifyJudgesPolicy = "NotifyJudges";
    public const int NotifyJudgesPermitLimit = 2;
    public static readonly TimeSpan NotifyJudgesWindow = TimeSpan.FromSeconds(30);

    public static IServiceCollection ConfigureRateLimiting(this IServiceCollection services)
    {
        // NotifyJudges is the only rate-limited endpoint. The default policy is a no-op.
        // The window is partitioned per organizer (JWT `sub`) so one organizer can never
        // throttle another. The endpoint requires ORGANIZER, so `sub` is always present there.
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(NotifyJudgesPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.User.FindFirst("sub")?.Value ?? string.Empty,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = NotifyJudgesPermitLimit,
                        Window = NotifyJudgesWindow,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                    }));
        });

        return services;
    }
}
