using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Errors;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.Features.Competitions;

/// <summary>Returns null when not found or not owned by the caller — endpoint maps that to a plain 404.</summary>
public sealed record UpdateCompetitionCommand(
    Guid Id,
    string Name,
    string Venue,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Description,
    string? LogoUrl,
    int? EntryLimit,
    DateOnly? RegistrationStart,
    DateOnly? RegistrationEnd) : IRequest<CompetitionDetailDto?>;

public sealed class UpdateCompetitionCommandValidator : AbstractValidator<UpdateCompetitionCommand>
{
    public UpdateCompetitionCommandValidator()
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

public sealed class UpdateCompetitionCommandHandler(AppDbContext dbContext, ICurrentUser currentUser)
    : IRequestHandler<UpdateCompetitionCommand, CompetitionDetailDto?>
{
    public async Task<CompetitionDetailDto?> Handle(UpdateCompetitionCommand request, CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.CreatedByUserId == currentUser.Sub, cancellationToken);

        if (competition is null)
        {
            return null;
        }

        if (competition.State is not (CompetitionState.Draft or CompetitionState.Active))
        {
            throw new DomainException(
                DomainErrorType.InvalidStateTransition,
                $"Competition cannot be edited while in state {competition.State}.");
        }

        competition.Name = request.Name;
        competition.Venue = request.Venue;
        competition.StartDate = request.StartDate;
        competition.EndDate = request.EndDate;
        competition.Description = request.Description;
        competition.LogoUrl = request.LogoUrl;
        competition.EntryLimit = request.EntryLimit;
        competition.StartRegistration = request.RegistrationStart;
        competition.EndRegistration = request.RegistrationEnd;

        await dbContext.SaveChangesAsync(cancellationToken);

        return CompetitionDetailDto.FromEntity(competition);
    }
}
