using MediatR;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.GetDispatchStatus;

internal static class GetDispatchStatusEndpoint
{
    internal static IEndpointRouteBuilder MapGetDispatchStatusEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/dispatch", async (Guid competitionId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetDispatchStatusQuery(competitionId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetDispatchStatus")
        .Produces<IReadOnlyList<DispatchStatusDto>>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
