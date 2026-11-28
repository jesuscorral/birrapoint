namespace BirraPoint.Common.Security;

/// <summary>Authorization and rate-limit policy names modules attach to their endpoints; the host defines what they mean.</summary>
public static class Policies
{
    /// <summary>Realm role <c>ORGANIZER</c>.</summary>
    public const string Organizer = "ORGANIZER";

    /// <summary>Realm role <c>JUDGE</c>.</summary>
    public const string Judge = "JUDGE";

    /// <summary>Rate-limit policy of the notify-judges endpoint (FR-065).</summary>
    public const string NotifyJudgesRateLimit = "NotifyJudges";
}
