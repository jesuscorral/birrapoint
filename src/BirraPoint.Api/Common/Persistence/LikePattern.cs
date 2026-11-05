namespace BirraPoint.Api.Common.Persistence;

/// <summary>Builds exact-match patterns for <c>EF.Functions.ILike</c>, the case-insensitive
/// comparison PostgreSQL can evaluate in SQL (Npgsql cannot translate
/// <c>string.Equals(..., StringComparison)</c>). LIKE wildcards in user input are escaped so
/// they match literally.</summary>
internal static class LikePattern
{
    public const string EscapeCharacter = @"\";

    public static string Escape(string value) =>
        value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
