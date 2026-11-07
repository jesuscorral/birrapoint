using FluentValidation;
using MediatR;

namespace BuildingBlocks.Validation.MediatR;

/// <summary>
/// Runs every registered <see cref="IValidator{T}"/> for the request before the handler; throws
/// FluentValidation's <see cref="ValidationException"/> on any failure, which the
/// validation mapping (MapValidation) turns into a 400 ProblemDetails. A no-op
/// when no validator is registered for the request type.
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        // Each validator gets its own ValidationContext: FluentValidation's rule engine
        // accumulates failures onto the context it's given, so sharing one instance across
        // validators run in parallel double-counts every failure.
        var results = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken))).ConfigureAwait(false);
        var failures = results.SelectMany(result => result.Errors).Where(failure => failure is not null).ToList();

        if (failures.Count != 0)
        {
            throw new ValidationException(failures);
        }

        return await next(cancellationToken).ConfigureAwait(false);
    }
}
