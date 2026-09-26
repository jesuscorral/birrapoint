using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BirraPoint.Api.IntegrationTests.Evaluations;

/// <summary>
/// Test-only <see cref="DbCommandInterceptor"/> that deterministically reproduces the concurrent-
/// commit races <c>SubmitEvaluationCommandHandler</c> guards against — without relying on two real
/// HTTP calls happening to interleave in exactly the right window (flaky). Registered once, always,
/// on <see cref="ApiFactory"/>'s <c>AppDbContext</c> (shared across every test in a given test
/// class), but inert unless a test arms it: production code never sees this type at all.
///
/// Usage: a test calls <see cref="ArmBeforeNext"/> with one of <c>SubmitEvaluationQueryTags</c>'
/// constants and an action; the NEXT database command whose EF <c>TagWith</c> SQL comment contains
/// that tag triggers the action — on its own connection, committed — immediately BEFORE that
/// command reaches Postgres, simulating a truly concurrent second request winning that exact race
/// window. Arm-and-fire is a single atomic claim (<see cref="Interlocked.CompareExchange"/> on an
/// immutable <c>Trigger</c> record), so exactly one matching command can ever consume a given arm —
/// no other command can partially observe or double-fire it. One-shot: firing clears the arm, and
/// the <see cref="ArmedTrigger"/> handle returned by <see cref="ArmBeforeNext"/> can also be
/// disarmed explicitly (directly or via <c>using</c>), so an arm that never fires cannot leak into a
/// later query in the same or a subsequent test.
/// </summary>
public sealed class SubmitEvaluationRaceInterceptor : DbCommandInterceptor
{
    private sealed record Trigger(string Tag, Func<CancellationToken, Task> Action);

    /// <summary>
    /// Handle returned by <see cref="ArmBeforeNext"/>. Exposes whether the arm actually fired, so a
    /// test can assert its race was genuinely exercised instead of passing vacuously; disposing it
    /// disarms the interceptor (a no-op if it already fired or was already disarmed).
    /// </summary>
    public sealed class ArmedTrigger(SubmitEvaluationRaceInterceptor owner) : IDisposable
    {
        private int _fired;

        public bool Fired => Volatile.Read(ref _fired) == 1;

        internal void MarkFired() => Volatile.Write(ref _fired, 1);

        public void Dispose() => owner.Disarm();
    }

    private Trigger? _armed;
    private ArmedTrigger? _handle;

    public ArmedTrigger ArmBeforeNext(string tag, Func<CancellationToken, Task> action)
    {
        var handle = new ArmedTrigger(this);
        _handle = handle;
        Interlocked.Exchange(ref _armed, new Trigger(tag, action));
        return handle;
    }

    /// <summary>Clears the current arm, if any. Safe to call even if it already fired.</summary>
    public void Disarm() => Interlocked.Exchange(ref _armed, null);

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        var trigger = Volatile.Read(ref _armed);
        if (trigger is not null
            && command.CommandText.Contains(trigger.Tag, StringComparison.Ordinal)
            && Interlocked.CompareExchange(ref _armed, null, trigger) == trigger)
        {
            // Won the atomic claim on this trigger: no other command can also fire it.
            _handle?.MarkFired();
            await trigger.Action(cancellationToken);
        }

        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
