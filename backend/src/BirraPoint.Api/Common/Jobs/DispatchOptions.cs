namespace BirraPoint.Api.Common.Jobs;

/// <summary>Bound from the <c>Dispatch</c> configuration section (T129).</summary>
public sealed class DispatchOptions
{
    public const string SectionName = "Dispatch";

    /// <summary>How long a claimed job stays owned without renewal. The worker renews at a third of
    /// this while the handler runs; a Running job past it is treated as abandoned.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
}
