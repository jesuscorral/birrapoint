using BuildingBlocks.ProblemDetails;

namespace BirraPoint.Api.Modules.Organization.Errors;

/// <summary>Thrown by a Organization slice to signal one of its catalogued domain errors (contracts/rest-api.md §Error catalog).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032:Implement standard exception constructors",
    Justification = "A OrganizationException always carries a catalogued OrganizationError; parameterless/message-only constructors would create untyped domain errors.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "RCS1194:Implement exception constructors",
    Justification = "Same as CA1032: every OrganizationException must carry a catalogued OrganizationError.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3871:Exception types should be public",
    Justification = "Never crosses the assembly boundary: it is mapped to RFC 7807 inside the API.")]
internal sealed class OrganizationException : Exception
{
    public OrganizationError ErrorType { get; }

    public IReadOnlyDictionary<string, object?> Extensions { get; }

    public OrganizationException(
        OrganizationError errorType,
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
        : base(detail ?? OrganizationErrorCatalog.Entries[errorType].Title)
    {
        ErrorType = errorType;
        Extensions = extensions ?? new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    /// <summary>Maps the exception to its catalogued ProblemDetails shape.</summary>
    internal static ProblemMapping ToProblem(OrganizationException exception)
    {
        var (urn, status, title) = OrganizationErrorCatalog.Entries[exception.ErrorType];
        return new ProblemMapping(urn, status, title, exception.Message, exception.Extensions);
    }
}
