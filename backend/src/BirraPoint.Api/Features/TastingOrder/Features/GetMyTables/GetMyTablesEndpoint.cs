using MediatR;

namespace BirraPoint.Api.Features.TastingOrder.Features.GetMyTables;

internal static class GetMyTablesEndpoint
{
    internal static IEndpointRouteBuilder MapGetMyTablesEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/", async (ISender sender, CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new GetMyTablesQuery(), cancellationToken)))
        .WithName("GetMyTables")
        .Produces<IReadOnlyList<JudgeTableSummaryDto>>();

        return endpointsGroup;
    }
}
