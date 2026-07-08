namespace BirraPoint.Api.Domain;

/// <summary>Failed after max attempts stays retryable via API (FR-041, R-06).</summary>
public enum DispatchJobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
}
