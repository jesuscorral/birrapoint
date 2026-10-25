using System.Text.Json;
using BirraPoint.Api.Features.Judges;

namespace BirraPoint.Api.UnitTests.Judges;

public sealed class NotifyJudgesRulesTests
{
    [Fact]
    public void InFlightPayloadsProducedByTheQueueAreRecognised()
    {
        var judgeA = Guid.NewGuid();
        var judgeB = Guid.NewGuid();
        // Same serialization the DispatchJobQueue uses for the payload object.
        var payloads = new[]
        {
            JsonSerializer.Serialize(new { JudgeId = judgeA }),
            JsonSerializer.Serialize(new SendInvitationPayload(judgeB)),
        };

        var ids = NotifyJudgesRules.JudgeIdsWithInFlightInvitation(payloads);

        Assert.Equal(new HashSet<Guid> { judgeA, judgeB }, ids);
    }

    [Fact]
    public void UnreadablePayloadsAreIgnored()
    {
        var ids = NotifyJudgesRules.JudgeIdsWithInFlightInvitation(["not json", "{}", "null"]);

        Assert.Empty(ids);
    }
}
