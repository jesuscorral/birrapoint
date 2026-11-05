using MediatR;

namespace BirraPoint.Api.Modules.Judging.Features.Monitoring.Features.GetProgress;

internal static class GetProgressEndpoint
{
    internal static IEndpointRouteBuilder MapGetProgressEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/progress", async (Guid competitionId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetProgressQuery(competitionId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetProgress")
        .Produces<IReadOnlyList<TableProgressSummaryDto>>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
