using FluentValidation;

namespace BirraPoint.Api.Features.Judges.Features.RegisterJudges;

internal sealed class RegisterJudgesCommandValidator : AbstractValidator<RegisterJudgesCommand>
{
    public RegisterJudgesCommandValidator()
    {
        RuleFor(c => c.Emails).NotEmpty();
        RuleForEach(c => c.Emails).NotEmpty().EmailAddress();
    }
}
