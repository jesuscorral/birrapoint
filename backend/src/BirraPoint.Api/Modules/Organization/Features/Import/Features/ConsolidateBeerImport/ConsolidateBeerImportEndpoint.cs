using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Import.Features.ConsolidateBeerImport;

internal static class ConsolidateBeerImportEndpoint
{
    internal static IEndpointRouteBuilder MapConsolidateBeerImportEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/{importId:guid}/consolidate", async (Guid competitionId, Guid importId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ConsolidateBeerImportCommand(competitionId, importId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ConsolidateBeerImport")
        .Produces<ConsolidateBeerImportResult>()
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
