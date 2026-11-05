namespace BirraPoint.Api.IntegrationTests.TestHost;

/// <summary>Builds the relative <see cref="Uri"/> values the test <see cref="HttpClient"/> calls expect (CA2234).</summary>
internal static class Urls
{
    public static Uri Rel(string path) => new(path, UriKind.Relative);
}
