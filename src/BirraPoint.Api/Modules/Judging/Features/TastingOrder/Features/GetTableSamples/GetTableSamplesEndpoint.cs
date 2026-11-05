using BirraPoint.Api.Modules.Judging.Features.TastingOrder.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Judging.Features.TastingOrder.Features.GetTableSamples;

internal static class GetTableSamplesEndpoint
{
    internal static IEndpointRouteBuilder MapGetTableSamplesEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/{tableId:guid}/samples", async (Guid tableId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetTableSamplesQuery(tableId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetTableSamples")
        .Produces<IReadOnlyList<JudgeSampleDto>>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
