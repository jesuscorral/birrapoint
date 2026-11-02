using BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Import.Features.GetBeerImport;

internal static class GetBeerImportEndpoint
{
    internal static IEndpointRouteBuilder MapGetBeerImportEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/{importId:guid}", async (Guid competitionId, Guid importId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetBeerImportQuery(competitionId, importId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetBeerImport")
        .Produces<BeerImportBatchDto>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
