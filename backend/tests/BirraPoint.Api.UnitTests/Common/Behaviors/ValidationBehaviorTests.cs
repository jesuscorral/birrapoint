using BirraPoint.Api.Common.Behaviors;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace BirraPoint.Api.UnitTests.Common.Behaviors;

public sealed class ValidationBehaviorTests
{
    private sealed record TestRequest(string Name) : IRequest<string>;

    private sealed class AlwaysPassesValidator : AbstractValidator<TestRequest>
    {
        public AlwaysPassesValidator() => RuleFor(r => r.Name).NotEmpty();
    }

    private sealed class AlwaysFailsValidator : AbstractValidator<TestRequest>
    {
        public override Task<ValidationResult> ValidateAsync(
            ValidationContext<TestRequest> context, CancellationToken cancellation = default) =>
            Task.FromResult(new ValidationResult([new ValidationFailure("Name", "Always fails.")]));
    }

    private sealed class AnotherAlwaysFailsValidator : AbstractValidator<TestRequest>
    {
        public override Task<ValidationResult> ValidateAsync(
            ValidationContext<TestRequest> context, CancellationToken cancellation = default) =>
            Task.FromResult(new ValidationResult([new ValidationFailure("Name", "Also always fails.")]));
    }

    [Fact]
    public async Task Calls_next_when_no_validators_are_registered_for_the_request()
    {
        var behavior = new ValidationBehavior<TestRequest, string>([]);
        var nextCalled = false;

        var result = await behavior.Handle(new TestRequest("x"), _ =>
        {
            nextCalled = true;
            return Task.FromResult("handled");
        }, CancellationToken.None);

        Assert.True(nextCalled);
        Assert.Equal("handled", result);
    }

    [Fact]
    public async Task Calls_next_when_the_validator_passes()
    {
        var behavior = new ValidationBehavior<TestRequest, string>([new AlwaysPassesValidator()]);

        var result = await behavior.Handle(new TestRequest("valid"), _ => Task.FromResult("handled"), CancellationToken.None);

        Assert.Equal("handled", result);
    }

    [Fact]
    public async Task Throws_validation_exception_and_never_calls_next_when_the_validator_fails()
    {
        var behavior = new ValidationBehavior<TestRequest, string>([new AlwaysFailsValidator()]);
        var nextCalled = false;

        var exception = await Assert.ThrowsAsync<ValidationException>(() => behavior.Handle(new TestRequest(""), _ =>
        {
            nextCalled = true;
            return Task.FromResult("handled");
        }, CancellationToken.None));

        Assert.False(nextCalled);
        Assert.Contains(exception.Errors, e => e.ErrorMessage == "Always fails.");
    }

    [Fact]
    public async Task Aggregates_failures_from_multiple_validators()
    {
        var behavior = new ValidationBehavior<TestRequest, string>(
        [
            new AlwaysFailsValidator(),
            new AnotherAlwaysFailsValidator(),
        ]);

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => behavior.Handle(new TestRequest(""), _ => Task.FromResult("handled"), CancellationToken.None));

        Assert.Equal(2, exception.Errors.Count());
    }
}
