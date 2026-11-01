using BirraPoint.Api.Features.Dispatch.Features.GetDispatchStatus;
using BirraPoint.Api.Features.Dispatch.Features.GetResultsArchive;
using BirraPoint.Api.Features.Dispatch.Features.RetryDispatch;

namespace BirraPoint.Api.Features.Dispatch;

/// <summary>Maps the Results &amp; dispatch endpoints (contracts/rest-api.md §Results &amp; dispatch) — ORGANIZER-only.</summary>
internal static class BaseDispatchEndpoints
{
    public static IEndpointRouteBuilder MapDispatchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions/{competitionId:guid}")
            .RequireAuthorization("ORGANIZER")
            .WithTags("Dispatch");

        group.MapGetResultsArchiveEndpoint()
            .MapGetDispatchStatusEndpoint()
            .MapRetryDispatchEndpoint();

        return endpoints;
    }
}
