using BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents;

namespace BirraPoint.Api.UnitTests.Modules.Organization.Import.CommonComponents;

public sealed class ImportEmailTests
{
    [Theory]
    [InlineData("ana@example.com", true)]
    [InlineData("a.b+c@sub.example.org", true)]
    [InlineData("no-at-sign", false)]
    [InlineData("two@@example.com", false)]
    [InlineData("missing@tld", false)]
    [InlineData("with space@example.com", false)]
    [InlineData("", false)]
    public void IsWellFormedAppliesTheSharedPattern(string email, bool expected) =>
        Assert.Equal(expected, ImportEmail.IsWellFormed(email));

    [Fact]
    public void MaxLengthMatchesTheStagingColumn() => Assert.Equal(320, ImportEmail.MaxLength);
}
