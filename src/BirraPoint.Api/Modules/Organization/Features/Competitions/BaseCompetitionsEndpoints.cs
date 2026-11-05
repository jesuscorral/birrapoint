using BirraPoint.Api.Modules.Organization.Features.Competitions.Features.ChangeCompetitionState;
using BirraPoint.Api.Modules.Organization.Features.Competitions.Features.CreateCompetition;
using BirraPoint.Api.Modules.Organization.Features.Competitions.Features.GetCompetition;
using BirraPoint.Api.Modules.Organization.Features.Competitions.Features.GetCompetitionCategories;
using BirraPoint.Api.Modules.Organization.Features.Competitions.Features.GetEntriesList;
using BirraPoint.Api.Modules.Organization.Features.Competitions.Features.ListCompetitions;
using BirraPoint.Api.Modules.Organization.Features.Competitions.Features.SetCompetitionCategories;
using BirraPoint.Api.Modules.Organization.Features.Competitions.Features.UpdateCompetition;

namespace BirraPoint.Api.Modules.Organization.Features.Competitions;

/// <summary>Maps every Competitions endpoint (contracts/rest-api.md §Competitions) — ORGANIZER-only.</summary>
internal static class BaseCompetitionsEndpoints
{
    public static IEndpointRouteBuilder MapCompetitionsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/competitions")
            .RequireAuthorization("ORGANIZER")
            .WithTags("Competitions");

        group.MapCreateCompetitionEndpoint()
            .MapListCompetitionsEndpoint()
            .MapGetCompetitionEndpoint()
            .MapUpdateCompetitionEndpoint()
            .MapChangeCompetitionStateEndpoint()
            .MapGetCompetitionCategoriesEndpoint()
            .MapSetCompetitionCategoriesEndpoint()
            .MapGetEntriesListEndpoint();

        return endpoints;
    }
}
