using BirraPoint.Api.Modules.Judging.Features.Tables.CommonComponents.Dtos;
using MediatR;

namespace BirraPoint.Api.Modules.Judging.Features.Tables.Features.CreateTable;

internal static class CreateTableEndpoint
{
    internal sealed record CreateTableRequest(string Name, IReadOnlyList<Guid> JudgeIds, IReadOnlyList<Guid> BeerEntryIds);

    internal static IEndpointRouteBuilder MapCreateTableEndpoint(this IEndpointRouteBuilder endpointsGroup)
    {
        endpointsGroup.MapPost("/", async (Guid competitionId, CreateTableRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var command = new CreateTableCommand(competitionId, request.Name, request.JudgeIds, request.BeerEntryIds);
            var result = await sender.Send(command, cancellationToken);
            return result is null ? Results.NotFound() : Results.Created($"/api/v1/competitions/{competitionId}/tables/{result.Id}", result);
        })
            .WithName("CreateTable")
            .Produces<TableMutationResult>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return endpointsGroup;
    }
}
