using MediatR;

namespace BirraPoint.Api.Features.Monitoring.Features.GetEntryEvaluations;

internal static class GetEntryEvaluationsEndpoint
{
    internal static IEndpointRouteBuilder MapGetEntryEvaluationsEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/entries/{entryId:guid}/evaluations", async (
            Guid competitionId, Guid entryId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetEntryEvaluationsQuery(competitionId, entryId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetEntryEvaluations")
        .Produces<EntryEvaluationsResult>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
