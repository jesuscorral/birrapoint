using BirraPoint.Api.Features.Judges.Features.GetJudges;
using BirraPoint.Api.Features.Judges.Features.NotifyJudges;
using BirraPoint.Api.Features.Judges.Features.RegisterJudges;
using BirraPoint.Api.Features.Judges.Features.ResendInvitation;
using BirraPoint.Api.Features.Judges.Features.UpdateJudgeEmail;

namespace BirraPoint.Api.Features.Judges;

/// <summary>Maps the Judges endpoints (contracts/rest-api.md §Judges) — ORGANIZER-only. The
/// judge roster import flow lives in Features/Import (BaseImportEndpoints).</summary>
internal static class BaseJudgesEndpoints
{
    public static IEndpointRouteBuilder MapJudgesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}/judges")
            .RequireAuthorization("ORGANIZER")
            .WithTags("Judges");

        group.MapRegisterJudgesEndpoint()
            .MapGetJudgesEndpoint()
            .MapUpdateJudgeEmailEndpoint()
            .MapResendInvitationEndpoint()
            .MapNotifyJudgesEndpoint();

        return endpoints;
    }
}
