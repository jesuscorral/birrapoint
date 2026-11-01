using BirraPoint.Api.Features.Import.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Features.Import.Features.GetJudgeImport;

internal static class GetJudgeImportEndpoint
{
    internal static IEndpointRouteBuilder MapGetJudgeImportEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/{importId:guid}", async (Guid competitionId, Guid importId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetJudgeImportQuery(competitionId, importId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetJudgeImport")
        .Produces<JudgeImportBatchDto>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
