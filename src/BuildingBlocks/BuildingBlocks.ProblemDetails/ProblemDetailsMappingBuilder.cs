using Microsoft.AspNetCore.Mvc;

namespace BuildingBlocks.ProblemDetails;

/// <summary>
/// Collects exception-to-problem mappings. Mappings are tried in registration order and the first
/// whose exception type matches wins, so register the most specific types first.
/// </summary>
public sealed class ProblemDetailsMappingBuilder
{
    private readonly List<Func<Exception, Microsoft.AspNetCore.Mvc.ProblemDetails?>> _mappings = [];

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
            var mapping = map(exception);
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
    /// </summary>
    public ProblemDetailsMappingBuilder MapProblem<TException>(Func<TException, Microsoft.AspNetCore.Mvc.ProblemDetails> map)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(map);
        _mappings.Add(exception => exception is TException typed ? map(typed) : null);
        return this;
    }

    internal Microsoft.AspNetCore.Mvc.ProblemDetails? TryMap(Exception exception)
    {
        foreach (var mapping in _mappings)
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
