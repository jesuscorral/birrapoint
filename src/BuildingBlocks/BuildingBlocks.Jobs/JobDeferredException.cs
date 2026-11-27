namespace BuildingBlocks.Jobs;

/// <summary>
/// Thrown by an <see cref="IJobHandler"/> that cannot run yet because something else must finish first. The
/// worker puts the job back to <see cref="JobStatus.Pending"/> to run again after <see cref="Delay"/>, without
/// counting an attempt or recording an error, so waiting never exhausts the retry budget.
/// </summary>
public sealed class JobDeferredException : Exception
{
    public JobDeferredException()
        : this(TimeSpan.FromSeconds(1))
    {
    }

    public JobDeferredException(string message)
        : base(message)
    {
        Delay = TimeSpan.FromSeconds(1);
    }

    public JobDeferredException(string message, Exception innerException)
        : base(message, innerException)
    {
        Delay = TimeSpan.FromSeconds(1);
    }

    public JobDeferredException(TimeSpan delay)
        : base("Job deferred.")
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(delay, TimeSpan.Zero);
        Delay = delay;
    }

    /// <summary>How long to wait before the job is eligible again.</summary>
    public TimeSpan Delay { get; }
}
