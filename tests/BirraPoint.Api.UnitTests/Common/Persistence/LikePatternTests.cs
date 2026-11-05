using BirraPoint.Api.Common.Persistence;

namespace BirraPoint.Api.UnitTests.Common.Persistence;

/// <summary>LikePattern.Escape turns a user value into an exact-match ILIKE pattern: LIKE
/// wildcards and the escape character itself must match literally.</summary>
public sealed class LikePatternTests
{
    [Theory]
    [InlineData("21A", "21A")]
    [InlineData("50%", @"50\%")]
    [InlineData("Table_1", @"Table\_1")]
    [InlineData(@"a\b", @"a\\b")]
    [InlineData(@"%_\", @"\%\_\\")]
    [InlineData("", "")]
    public void EscapeMakesWildcardsAndTheEscapeCharacterLiteral(string value, string expected) =>
        Assert.Equal(expected, LikePattern.Escape(value));

    [Fact]
    public void EscapeCharacterIsABackslash() =>
        Assert.Equal(@"\", LikePattern.EscapeCharacter);
}
