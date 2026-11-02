namespace BirraPoint.Api.Modules.Organization.Contracts;

/// <summary>Read surface over competition judges for other modules that only need to display them
/// (audit drill-down, score sheets).</summary>
internal interface IJudgeDirectory
{
    /// <summary>Display names keyed by judge id; unknown ids are absent.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> judgeIds, CancellationToken cancellationToken = default);
}
