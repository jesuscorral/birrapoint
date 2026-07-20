namespace BirraPoint.Api.Features.Judges;

public sealed record JudgeRegistrationPlan(IReadOnlyList<string> ToCreate, IReadOnlyList<JudgeSkipDto> Skipped);

/// <summary>
/// Pure classification of a submitted email list (FR-014/FR-015): in-list duplicates (case-
/// insensitive, first occurrence wins) are skipped before already-registered emails are checked,
/// so a duplicate of an already-registered address is reported once (on its first occurrence) and
/// as a plain duplicate afterward.
/// </summary>
public static class JudgeRegistrationPlanner
{
    public static JudgeRegistrationPlan Plan(IReadOnlyList<string> emails, IReadOnlySet<string> existingEmails)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toCreate = new List<string>();
        var skipped = new List<JudgeSkipDto>();

        foreach (var email in emails)
        {
            if (!seen.Add(email))
            {
                skipped.Add(new JudgeSkipDto(email, "duplicate-in-list"));
                continue;
            }

            if (existingEmails.Contains(email))
            {
                skipped.Add(new JudgeSkipDto(email, "already-registered"));
                continue;
            }

            toCreate.Add(email);
        }

        return new JudgeRegistrationPlan(toCreate, skipped);
    }
}
