using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Singleton behind the dispatcher's wake-up channel, plus the per-<c>DbContext</c> bookkeeping of "this context
/// staged outbox rows that are not committed yet". The interceptors mark a context when a save includes outbox
/// rows and signal only once the commit is known, so a rolled-back transaction never wakes the dispatcher.
/// </summary>
internal sealed class OutboxWakeUp
{
    private readonly Channel<byte> _channel = Channel.CreateUnbounded<byte>();
    private readonly ConditionalWeakTable<DbContext, object> _pending = new();

    public ChannelReader<byte> Reader => _channel.Reader;

    public void Signal() => _channel.Writer.TryWrite(0);

    public void MarkPending(DbContext context) => _pending.TryAdd(context, new object());

    /// <summary>Forgets the context's staged rows (rollback or failure).</summary>
    public void ClearPending(DbContext context) => _pending.Remove(context);

    /// <summary>Signals once if the context had staged outbox rows; call after a commit.</summary>
    public void SignalIfPending(DbContext context)
    {
        if (_pending.Remove(context))
        {
            Signal();
        }
    }
}
