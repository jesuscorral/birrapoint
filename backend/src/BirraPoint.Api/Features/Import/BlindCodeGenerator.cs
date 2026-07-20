namespace BirraPoint.Api.Features.Import;

/// <summary>Generates unique alphanumeric blind codes at consolidation (FR-013).</summary>
public static class BlindCodeGenerator
{
    // Excludes 0/O/1/I to avoid visually ambiguous codes on printed judge sheets.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 6;

    /// <summary>
    /// Generates a code not already present in <paramref name="existingCodes"/> and adds it to
    /// the set before returning, so the caller can pass the same set across a whole batch and get
    /// collision-safe codes without a second lookup.
    /// </summary>
    public static string GenerateUnique(ISet<string> existingCodes, Random random)
    {
        string code;
        do
        {
            code = new string(Enumerable.Range(0, Length).Select(_ => Alphabet[random.Next(Alphabet.Length)]).ToArray());
        }
        while (!existingCodes.Add(code));

        return code;
    }
}
