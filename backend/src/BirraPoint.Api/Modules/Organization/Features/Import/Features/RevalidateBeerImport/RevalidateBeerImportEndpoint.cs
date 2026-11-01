using BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Import.Features.RevalidateBeerImport;

internal static class RevalidateBeerImportEndpoint
{
    internal static IEndpointRouteBuilder MapRevalidateBeerImportEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/{importId:guid}/revalidate", async (Guid competitionId, Guid importId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new RevalidateBeerImportCommand(competitionId, importId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("RevalidateBeerImport")
        .Produces<BeerImportBatchDto>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
