namespace BirraPoint.Api.Common.Errors;

/// <summary>Urn/status/title metadata for each <see cref="HostError"/>, per contracts/rest-api.md §Error catalog.</summary>
internal static class HostErrorCatalog
{
    public static readonly IReadOnlyDictionary<HostError, (string Urn, int Status, string Title)> Entries =
        new Dictionary<HostError, (string, int, string)>
        {
            [HostError.Validation] = ("urn:birrapoint:validation", StatusCodes.Status400BadRequest, "Validation failed"),
            [HostError.RateLimited] = ("urn:birrapoint:rate-limited", StatusCodes.Status429TooManyRequests, "Too many requests"),
        };
}
