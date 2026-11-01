using BirraPoint.Api.Features.Import.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Features.Import.Features.GetImport;

internal static class GetImportEndpoint
{
    internal static IEndpointRouteBuilder MapGetImportEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/{importId:guid}", async (Guid competitionId, Guid importId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetImportQuery(competitionId, importId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetImport")
        .Produces<ImportBatchDto>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
