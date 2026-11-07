namespace BuildingBlocks.ProblemDetails;

/// <summary>The RFC 7807 shape an exception is mapped to.</summary>
/// <param name="Type">The problem <c>type</c> URI (e.g. a URN from the host's error catalog).</param>
/// <param name="Status">The HTTP status code, also written to the response.</param>
/// <param name="Title">The short, human-readable <c>title</c>.</param>
/// <param name="Detail">The optional <c>detail</c>.</param>
/// <param name="Extensions">Optional extension members copied onto the problem body.</param>
public sealed record ProblemMapping(
    string Type,
    int Status,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, object?>? Extensions = null);
