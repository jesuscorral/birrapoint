using FluentValidation;

namespace BirraPoint.Api.Modules.Publication.Features.Dispatch.Features.RetryDispatch;

internal sealed class RetryDispatchCommandValidator : AbstractValidator<RetryDispatchCommand>
{
    public RetryDispatchCommandValidator()
    {
        RuleFor(c => c.ParticipantIds).NotEmpty();
    }
}
