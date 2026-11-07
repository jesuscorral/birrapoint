namespace BirraPoint.Api.Common.Errors;

/// <summary>
/// The cross-cutting error types the host itself raises (validation pipeline, rate limiter), a
/// subset of the closed list in contracts/rest-api.md §Error catalog. Domain errors live in each
/// module's <c>Errors</c> namespace (ADR-0033/ADR-0037); adding a member requires a contract
/// amendment (Principle VI).
/// </summary>
internal enum HostError
{
    Validation,
    RateLimited,
}
