using BuildingBlocks.ProblemDetails;

namespace BirraPoint.Api.Modules.Judging.Errors;

/// <summary>Thrown by a Judging slice to signal one of its catalogued domain errors (contracts/rest-api.md §Error catalog).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032:Implement standard exception constructors",
    Justification = "A JudgingException always carries a catalogued JudgingError; parameterless/message-only constructors would create untyped domain errors.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "RCS1194:Implement exception constructors",
    Justification = "Same as CA1032: every JudgingException must carry a catalogued JudgingError.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3871:Exception types should be public",
    Justification = "Never crosses the assembly boundary: it is mapped to RFC 7807 inside the API.")]
internal sealed class JudgingException : Exception
{
    public JudgingError ErrorType { get; }

    public IReadOnlyDictionary<string, object?> Extensions { get; }

    public JudgingException(
        JudgingError errorType,
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
        : base(detail ?? JudgingErrorCatalog.Entries[errorType].Title)
    {
        ErrorType = errorType;
        Extensions = extensions ?? new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    /// <summary>Maps the exception to its catalogued ProblemDetails shape.</summary>
    internal static ProblemMapping ToProblem(JudgingException exception)
    {
        var (urn, status, title) = JudgingErrorCatalog.Entries[exception.ErrorType];
        return new ProblemMapping(urn, status, title, exception.Message, exception.Extensions);
    }
}
