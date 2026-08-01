using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using FluentValidation;
using MediatR;

namespace BirraPoint.Api.Features.Competitions;

public sealed record CreateCompetitionCommand(
    string Name,
    string Venue,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Description,
    string? LogoUrl,
    int? EntryLimit,
    DateOnly? RegistrationStart,
    DateOnly? RegistrationEnd) : IRequest<CompetitionDetailDto>;

public sealed class CreateCompetitionCommandValidator : AbstractValidator<CreateCompetitionCommand>
{
    public CreateCompetitionCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Venue).NotEmpty().MaximumLength(200);
        RuleFor(c => c.EndDate)
            .GreaterThanOrEqualTo(c => c.StartDate)
            .WithMessage("EndDate must be on or after StartDate.");
        RuleFor(c => c.EntryLimit)
            .GreaterThan(0)
            .When(c => c.EntryLimit.HasValue);
        RuleFor(c => c.RegistrationEnd)
            .GreaterThanOrEqualTo(c => c.RegistrationStart!.Value)
            .When(c => c.RegistrationStart.HasValue && c.RegistrationEnd.HasValue)
            .WithMessage("RegistrationEnd must be on or after RegistrationStart.");
    }
}

public sealed class CreateCompetitionCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<CreateCompetitionCommand, CompetitionDetailDto>
{
    public async Task<CompetitionDetailDto> Handle(CreateCompetitionCommand request, CancellationToken cancellationToken)
    {
        // Resolve-or-create the caller's Organizer row (self-registration means it may not exist
        // yet on this, its first authenticated write). CreatedByUserId below stays the source of
        // truth for every existing ownership check; OrganizerId is additive (see Competition.cs).
        var organizer = await currentUser.GetOrganizerAsync(cancellationToken);

        var competition = new Competition
        {
            Name = request.Name,
            Venue = request.Venue,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Description = request.Description,
            LogoUrl = request.LogoUrl,
            EntryLimit = request.EntryLimit,
            StartRegistration = request.RegistrationStart,
            EndRegistration = request.RegistrationEnd,
            CreatedByUserId = currentUser.Sub,
            OrganizerId = organizer.Id,
        };

        dbContext.Competitions.Add(competition);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CompetitionDetailDto.FromEntity(competition);
    }
}
