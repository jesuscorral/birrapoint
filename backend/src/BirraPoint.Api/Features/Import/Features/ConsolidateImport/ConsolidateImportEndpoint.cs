using MediatR;

namespace BirraPoint.Api.Features.Import.Features.ConsolidateImport;

internal static class ConsolidateImportEndpoint
{
    internal static IEndpointRouteBuilder MapConsolidateImportEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/{importId:guid}/consolidate", async (Guid competitionId, Guid importId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ConsolidateImportCommand(competitionId, importId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ConsolidateImport")
        .Produces<ConsolidateImportResult>()
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
