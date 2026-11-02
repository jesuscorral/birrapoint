using System.Text.Json;

namespace BirraPoint.Api.Modules.Organization.Features.Judges.Features.NotifyJudges;

/// <summary>Pure rules for NotifyJudges (FR-059), unit-testable without Postgres.</summary>
internal static class NotifyJudgesRules
{
    /// <summary>
    /// Extracts the judge ids carried by in-flight SendInvitation payloads (<see cref="SendInvitationPayload"/>).
    /// Unreadable payloads are ignored — they cannot be attributed to a judge.
    /// </summary>
    public static HashSet<Guid> JudgeIdsWithInFlightInvitation(IEnumerable<string> inFlightPayloadJson)
    {
        var judgeIds = new HashSet<Guid>();
        foreach (var json in inFlightPayloadJson)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<SendInvitationPayload>(json);
                if (payload is not null && payload.JudgeId != Guid.Empty)
                {
                    judgeIds.Add(payload.JudgeId);
                }
            }
            catch (JsonException)
            {
                // Not a SendInvitation payload we understand; skip.
            }
        }

        return judgeIds;
    }
}
