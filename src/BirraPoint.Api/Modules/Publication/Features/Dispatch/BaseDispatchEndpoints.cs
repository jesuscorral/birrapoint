using BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.GetDispatchStatus;
using BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.GetResultsArchive;
using BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.RetryDispatch;
using BirraPoint.Common.Security;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch;

/// <summary>Maps the Results &amp; dispatch endpoints (contracts/rest-api.md §Results &amp; dispatch) — ORGANIZER-only.</summary>
internal static class BaseDispatchEndpoints
{
    public static IEndpointRouteBuilder MapDispatchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}")
            .RequireAuthorization(Policies.Organizer)
            .WithTags("Dispatch");

        group.MapGetResultsArchiveEndpoint()
            .MapGetDispatchStatusEndpoint()
            .MapRetryDispatchEndpoint();

        return endpoints;
    }
}
