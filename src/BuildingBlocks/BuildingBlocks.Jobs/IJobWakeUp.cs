using System.Threading.Channels;

namespace BuildingBlocks.Jobs;

/// <summary>Best-effort signal that wakes the worker for an immediate sweep. Call after the DB commit; it
/// never blocks or throws, and the worker's scheduled wake plus safety-net poll still cover a missed signal.</summary>
public interface IJobWakeUp
{
    void Signal();
}

/// <summary>Singleton backing <see cref="IJobWakeUp"/>; the worker waits on <see cref="Reader"/>.</summary>
internal sealed class JobWakeUp : IJobWakeUp
{
    private readonly Channel<byte> _channel = Channel.CreateUnbounded<byte>();

    public ChannelReader<byte> Reader => _channel.Reader;

    public void Signal() => _channel.Writer.TryWrite(0);
}
