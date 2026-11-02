using MediatR;

namespace BirraPoint.Api.Modules.Organization.Features.Import.Features.ConsolidateJudgeImport;

internal static class ConsolidateJudgeImportEndpoint
{
    internal static IEndpointRouteBuilder MapConsolidateJudgeImportEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/{importId:guid}/consolidate", async (Guid competitionId, Guid importId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ConsolidateJudgeImportCommand(competitionId, importId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ConsolidateJudgeImport")
        .Produces<ConsolidateJudgeImportResult>()
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
