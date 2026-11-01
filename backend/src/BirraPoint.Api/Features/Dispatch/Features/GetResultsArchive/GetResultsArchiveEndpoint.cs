using MediatR;

namespace BirraPoint.Api.Features.Dispatch.Features.GetResultsArchive;

internal static class GetResultsArchiveEndpoint
{
    internal static IEndpointRouteBuilder MapGetResultsArchiveEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapGet("/results/archive", async (Guid competitionId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetResultsArchiveQuery(competitionId), cancellationToken);
            if (result is null)
            {
                return Results.NotFound();
            }

            return result.ZipBytes is not null
                ? Results.File(result.ZipBytes, "application/zip", "results.zip")
                : Results.Json(new { status = result.Status }, statusCode: StatusCodes.Status202Accepted);
        })
        .WithName("GetResultsArchive")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status404NotFound);

        return endpointsGroup;
    }
}
