using System.Text.RegularExpressions;

namespace BirraPoint.Api.Modules.Organization.Features.Import.CommonComponents;

/// <summary>The email rule shared by the beer and judge imports: parsing and inline row edits must agree.</summary>
internal static class ImportEmail
{
    internal const int MaxLength = 320;

    private static readonly Regex Pattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    internal static bool IsWellFormed(string email) => Pattern.IsMatch(email);
}
