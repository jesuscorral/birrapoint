using BirraPoint.Api.IntegrationTests.TestHost;
using BuildingBlocks.EmailSender;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BirraPoint.Api.IntegrationTests.Email;

/// <summary>
/// BirraPoint wires BuildingBlocks.EmailSender (ADR-0035) to the Smtp section and supplies its own
/// sender address when configuration has none, as before the block existed (local Mailpit only sets
/// Smtp:Host/Smtp:Port).
/// </summary>
public sealed class EmailSenderRegistrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void DefaultsTheSenderToBirraPointsNoReplyAddress()
    {
        var options = factory.Services.GetRequiredService<IOptions<SmtpOptions>>().Value;

        Assert.Equal("BirraPoint <no-reply@birrapoint.local>", options.From);
    }
}
