using BirraPoint.Api.Features.Evaluations.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Features.Evaluations.Features.GetMyDiscrepancies;

internal static class GetMyDiscrepanciesEndpoint
{
    internal static IEndpointRouteBuilder MapGetMyDiscrepanciesEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        // Path matches contracts/rest-api.md §Judge workspace exactly: GET /me/tables/{tableId}/discrepancies
        // (a sibling of /evaluations, not nested under it).
        endpointsGroup.MapGet("/discrepancies", async (Guid tableId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetMyDiscrepanciesQuery(tableId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetMyDiscrepancies")
        .Produces<IReadOnlyList<DiscrepancyView>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
