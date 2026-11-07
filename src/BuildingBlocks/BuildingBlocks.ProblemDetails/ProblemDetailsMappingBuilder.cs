using Microsoft.AspNetCore.Mvc;

namespace BuildingBlocks.ProblemDetails;

/// <summary>
/// Collects exception-to-problem mappings. Mappings are tried in registration order and the first
/// whose exception type matches wins, so register the most specific types first. The table is
/// frozen the first time an exception is mapped; <c>Map</c>/<c>MapProblem</c> then throw
/// <see cref="InvalidOperationException"/>, so register every mapping during startup.
/// </summary>
public sealed class ProblemDetailsMappingBuilder
{
    private readonly Lock _gate = new();
    private readonly List<Func<Exception, Microsoft.AspNetCore.Mvc.ProblemDetails?>> _mappings = [];
    private Func<Exception, Microsoft.AspNetCore.Mvc.ProblemDetails?>[]? _frozen;

    internal ProblemDetailsMappingBuilder()
    {
    }

    /// <summary>Maps <typeparamref name="TException"/> to a <see cref="ProblemMapping"/>.</summary>
    public ProblemDetailsMappingBuilder Map<TException>(Func<TException, ProblemMapping> map)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(map);
        return MapProblem<TException>(exception =>
        {
            var mapping = map(exception)
                ?? throw new InvalidOperationException(
                    $"The mapping factory for {typeof(TException).Name} returned null; it must return a {nameof(ProblemMapping)}.");
            var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Type = mapping.Type,
                Title = mapping.Title,
                Status = mapping.Status,
                Detail = mapping.Detail,
            };

            if (mapping.Extensions is not null)
            {
                foreach (var (key, value) in mapping.Extensions)
                {
                    problem.Extensions[key] = value;
                }
            }

            return problem;
        });
    }

    /// <summary>
    /// Maps <typeparamref name="TException"/> to a ready-built <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails"/> (e.g. a
    /// <see cref="ValidationProblemDetails"/>). Its <c>Status</c> is written to the response.
    /// The factory must not return null: a null result throws <see cref="InvalidOperationException"/>
    /// naming the exception type (the fallback handler still answers the client with a generic 500).
    /// Throws <see cref="InvalidOperationException"/> once the table is frozen.
    /// </summary>
    public ProblemDetailsMappingBuilder MapProblem<TException>(Func<TException, Microsoft.AspNetCore.Mvc.ProblemDetails> map)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(map);
        lock (_gate)
        {
            if (_frozen is not null)
            {
                throw new InvalidOperationException(
                    "Problem mappings are frozen after the first exception is mapped; register them during startup.");
            }

            _mappings.Add(exception => exception is TException typed
                ? map(typed) ?? throw new InvalidOperationException(
                    $"The mapping factory for {typeof(TException).Name} returned null; it must return a ProblemDetails.")
                : null);
        }

        return this;
    }

    internal Microsoft.AspNetCore.Mvc.ProblemDetails? TryMap(Exception exception)
    {
        var table = _frozen;
        if (table is null)
        {
            lock (_gate)
            {
                table = _frozen ??= [.. _mappings];
            }
        }

        foreach (var mapping in table)
        {
            var problem = mapping(exception);
            if (problem is not null)
            {
                return problem;
            }
        }

        return null;
    }
}
