using MediatR;

namespace BirraPoint.Api.Features.Judges;

/// <summary>Maps every Judges endpoint (contracts/rest-api.md §Judges, T042) — ORGANIZER-only.</summary>
public static class JudgesEndpoints
{
    public static IEndpointRouteBuilder MapJudgesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}/judges")
            .RequireAuthorization("ORGANIZER")
            .WithTags("Judges");

        group.MapPost("/", async (Guid competitionId, RegisterJudgesRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new RegisterJudgesCommand(competitionId, request.Emails), cancellationToken);
            return result is null ? Results.NotFound() : Results.Created($"/api/v1/competitions/{competitionId}/judges", result);
        })
        .WithName("RegisterJudges")
        .Produces<RegisterJudgesResult>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/", async (Guid competitionId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new GetJudgesQuery(competitionId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("GetJudges")
        .Produces<IReadOnlyList<JudgeProfileDto>>()
        .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{judgeId:guid}", async (
            Guid competitionId, Guid judgeId, UpdateJudgeEmailRequest request, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new UpdateJudgeEmailCommand(competitionId, judgeId, request.Email), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("UpdateJudgeEmail")
        .Produces<JudgeProfileDto>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/{judgeId:guid}/invitation", async (Guid competitionId, Guid judgeId, ISender sender, CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(new ResendInvitationCommand(competitionId, judgeId), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithName("ResendInvitation")
        .Produces<ResendInvitationResult>()
        .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
