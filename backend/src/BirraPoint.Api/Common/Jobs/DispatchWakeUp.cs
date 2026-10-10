using System.Threading.Channels;

namespace BirraPoint.Api.Common.Jobs;

/// <summary>Best-effort signal that wakes the <see cref="DispatchWorker"/> for an immediate sweep.
/// Call after the DB commit; it never blocks or throws, and the worker's scheduled wake plus
/// safety-net poll still cover a missed signal.</summary>
public interface IDispatchWakeUp
{
    void Signal();
}

public sealed class ChannelDispatchWakeUp(Channel<Guid> channel) : IDispatchWakeUp
{
    public void Signal() => channel.Writer.TryWrite(Guid.Empty);
}
