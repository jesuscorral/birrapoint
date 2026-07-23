namespace BirraPoint.Api.Features.Dispatch;

/// <summary>
/// FR-040 results archive layout: <c>{CompetitionName}/{ParticipantId}/{StyleCode}_{BlindCode}.pdf</c>.
/// No sanitization of <paramref name="competitionName"/>: unlike a filesystem path, a ZIP entry
/// name has no reserved characters that would break the archive — a name containing '/' just
/// produces extra nesting inside the participant's folder, which is harmless and still opens fine
/// in any ZIP reader (verified by the "awkward characters" test below).
/// </summary>
public static class DispatchPaths
{
    public static string ZipEntryPath(string competitionName, Guid participantId, string styleCode, string blindCode) =>
        $"{competitionName}/{participantId}/{styleCode}_{blindCode}.pdf";
}
