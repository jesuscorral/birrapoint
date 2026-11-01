using BirraPoint.Api.Features.Import.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Features.Import.Features.UploadBeerImport;

internal static class UploadBeerImportEndpoint
{
    internal static IEndpointRouteBuilder MapUploadBeerImportEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/", async (Guid competitionId, IFormFile file, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new UploadBeerImportCommand(competitionId, file), cancellationToken);
            return result is null
                ? Results.NotFound()
                : Results.Created($"/api/v1/competitions/{competitionId}/imports/beers/{result.ImportId}", result);
        })
        .WithName("UploadBeerImport")
        .DisableAntiforgery()
        .Produces<BeerImportBatchDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
