using MediatR;

namespace BirraPoint.Api.Features.Competitions;

/// <summary>Maps every Competitions endpoint (contracts/rest-api.md §Competitions, T027/T028) — ORGANIZER-only.</summary>
public static class CompetitionsEndpoints
{
    public static IEndpointRouteBuilder MapCompetitionsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions")
            .RequireAuthorization("ORGANIZER")
            .WithTags("Competitions");

        group.MapPost("/", async (CreateCompetitionCommand command, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command, cancellationToken);
            return Results.Created($"/api/v1/competitions/{result.Id}", result);
        })
        .WithName("CreateCompetition")
        .Produces<CompetitionDetailDto>(StatusCodes.Status201Created);

        group.MapGet("/", async (ISender sender, CancellationToken cancellationToken) =>
            Results.Ok(await sender.Send(new ListCompetitionsQuery(), cancellationToken)))
        .WithName("ListCompetitions")
        .Produces<IReadOnlyList<CompetitionSummaryDto>>();

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetCompetitionQuery(id), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetCompetition")
        .Produces<CompetitionDetailDto>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (Guid id, UpdateCompetitionCommand command, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command with { Id = id }, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("UpdateCompetition")
        .Produces<CompetitionDetailDto>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/state", async (Guid id, ChangeCompetitionStateCommand command, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(command with { Id = id }, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ChangeCompetitionState")
        .Produces<ChangeCompetitionStateResult>()
        .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
