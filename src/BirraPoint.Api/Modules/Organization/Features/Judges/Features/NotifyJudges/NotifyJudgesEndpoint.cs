using BirraPoint.Api.Common.RateLimiting;
using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Judges.Features.NotifyJudges;

internal static class NotifyJudgesEndpoint
{
    internal static IEndpointRouteBuilder MapNotifyJudgesEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/notify", async (Guid competitionId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new NotifyJudgesCommand(competitionId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("NotifyJudges")
        .Produces<NotifyJudgesResult>()
        .Produces(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(RateLimitingExtensions.NotifyJudgesPolicy);

        return endpointsGroup;
    }
}
