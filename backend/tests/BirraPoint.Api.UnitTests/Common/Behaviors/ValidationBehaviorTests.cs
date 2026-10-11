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

    // Real AbstractValidator subclasses using the normal RuleFor DSL (not an overridden
    // ValidateAsync) — these are what actually reproduce a shared-ValidationContext bug, since
    // FluentValidation's rule engine accumulates failures onto the context it's given.
    private sealed class MinimumLengthValidator : AbstractValidator<TestRequest>
    {
        public MinimumLengthValidator() => RuleFor(r => r.Name).MinimumLength(5).WithMessage("Too short.");
    }

    private sealed class NoSpacesValidator : AbstractValidator<TestRequest>
    {
        public NoSpacesValidator() => RuleFor(r => r.Name).Must(n => !n.Contains(' ', StringComparison.Ordinal)).WithMessage("No spaces allowed.");
    }

    [Fact]
    public async Task CallsNextWhenNoValidatorsAreRegisteredForTheRequest()
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
    public async Task CallsNextWhenTheValidatorPasses()
    {
        var behavior = new ValidationBehavior<TestRequest, string>([new AlwaysPassesValidator()]);

        var result = await behavior.Handle(new TestRequest("valid"), _ => Task.FromResult("handled"), CancellationToken.None);

        Assert.Equal("handled", result);
    }

    [Fact]
    public async Task ThrowsValidationExceptionAndNeverCallsNextWhenTheValidatorFails()
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
    public async Task AggregatesFailuresFromMultipleValidators()
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

    [Fact]
    public async Task DoesNotDoubleCountFailuresFromRealValidatorsRunInParallel()
    {
        // " " (a single space) fails both real validators: too short (length 1 < 5) and contains
        // a space. Each validator must see its own ValidationContext — sharing one across the
        // Task.WhenAll causes FluentValidation's rule engine to accumulate onto the same failure
        // list, doubling the count.
        var behavior = new ValidationBehavior<TestRequest, string>(
        [
            new MinimumLengthValidator(),
            new NoSpacesValidator(),
        ]);

        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => behavior.Handle(new TestRequest(" "), _ => Task.FromResult("handled"), CancellationToken.None));

        Assert.Equal(2, exception.Errors.Count());
    }
}
