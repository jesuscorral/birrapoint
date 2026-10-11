using BirraPoint.Api.Features.Judges;

namespace BirraPoint.Api.UnitTests.Judges;

/// <summary>T038: bulk registration email format validation (FR-014).</summary>
public sealed class RegisterJudgesCommandValidatorTests
{
    private static readonly RegisterJudgesCommandValidator Validator = new();

    [Fact]
    public void CommandWithValidEmailsIsValid()
    {
        var result = Validator.Validate(new RegisterJudgesCommand(Guid.NewGuid(), ["alice@example.com", "bob@example.com"]));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CommandWithEmptyEmailListIsInvalid()
    {
        var result = Validator.Validate(new RegisterJudgesCommand(Guid.NewGuid(), []));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterJudgesCommand.Emails));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing-domain@")]
    [InlineData("@missing-local.com")]
    [InlineData("")]
    public void CommandWithMalformedEmailIsInvalid(string email)
    {
        var result = Validator.Validate(new RegisterJudgesCommand(Guid.NewGuid(), [email]));

        Assert.False(result.IsValid);
    }
}
