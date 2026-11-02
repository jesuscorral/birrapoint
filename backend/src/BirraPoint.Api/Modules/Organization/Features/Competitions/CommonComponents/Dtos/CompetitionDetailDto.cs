using BirraPoint.Api.Modules.Organization.Domain;

namespace BirraPoint.Api.Modules.Organization.Features.Competitions.CommonComponents.Dtos;

/// <summary>Full competition detail wire shape (contracts/rest-api.md §Competitions).</summary>
internal sealed record CompetitionDetailDto(
    Guid Id,
    string Name,
    string Venue,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Description,
    string? LogoUrl,
    int? EntryLimit,
    DateOnly? RegistrationStart,
    DateOnly? RegistrationEnd,
    CompetitionState State)
{
    public static CompetitionDetailDto FromEntity(Competition competition) => new(
        competition.Id,
        competition.Name,
        competition.Venue,
        competition.StartDate,
        competition.EndDate,
        competition.Description,
        competition.LogoUrl,
        competition.EntryLimit,
        competition.StartRegistration,
        competition.EndRegistration,
        competition.State);
}
