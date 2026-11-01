using BirraPoint.Api.Features.Import.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Features.Import.Features.ExcludeJudgeImportRow;

internal static class ExcludeJudgeImportRowEndpoint
{
    internal static IEndpointRouteBuilder MapExcludeJudgeImportRowEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/{importId:guid}/rows/{rowNumber:int}/exclude", async (
            Guid competitionId, Guid importId, int rowNumber, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ExcludeJudgeImportRowCommand(competitionId, importId, rowNumber), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ExcludeJudgeImportRow")
        .Produces<JudgeImportRowDto>()
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
