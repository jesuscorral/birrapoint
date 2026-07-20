using BirraPoint.Api.Features.Judges;

namespace BirraPoint.Api.UnitTests.Judges;

/// <summary>T038: bulk registration email format validation (FR-014).</summary>
public sealed class RegisterJudgesCommandValidatorTests
{
    private static readonly RegisterJudgesCommandValidator Validator = new();

    [Fact]
    public void Command_with_valid_emails_is_valid()
    {
        var result = Validator.Validate(new RegisterJudgesCommand(Guid.NewGuid(), ["alice@example.com", "bob@example.com"]));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Command_with_empty_email_list_is_invalid()
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
    public void Command_with_malformed_email_is_invalid(string email)
    {
        var result = Validator.Validate(new RegisterJudgesCommand(Guid.NewGuid(), [email]));

        Assert.False(result.IsValid);
    }
}
