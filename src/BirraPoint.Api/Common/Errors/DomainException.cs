namespace BirraPoint.Api.Common.Errors;

/// <summary>Thrown by a slice to signal one of the 14 catalogued domain errors (contracts/rest-api.md).</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032:Implement standard exception constructors",
    Justification = "A DomainException always carries a catalogued DomainErrorType; parameterless/message-only constructors would create untyped domain errors.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "RCS1194:Implement exception constructors",
    Justification = "Same as CA1032: every DomainException must carry a catalogued DomainErrorType.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3871:Exception types should be public",
    Justification = "Never crosses the assembly boundary: it is mapped to RFC 7807 inside the API.")]
internal sealed class DomainException : Exception
{
    public DomainErrorType ErrorType { get; }

    public IReadOnlyDictionary<string, object?> Extensions { get; }

    public DomainException(
        DomainErrorType errorType,
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
        : base(detail ?? DomainErrorCatalog.Entries[errorType].Title)
    {
        ErrorType = errorType;
        Extensions = extensions ?? new Dictionary<string, object?>(StringComparer.Ordinal);
    }
}
