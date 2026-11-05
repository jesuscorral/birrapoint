using FluentValidation;

namespace BirraPoint.Api.Modules.Organization.Features.Competitions.Features.UpdateCompetition;

internal sealed class UpdateCompetitionCommandValidator : AbstractValidator<UpdateCompetitionCommand>
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
