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
/// window. One-shot: disarms itself as soon as it fires, so it never leaks into a later query in the
/// same or a subsequent test.
/// </summary>
public sealed class SubmitEvaluationRaceInterceptor : DbCommandInterceptor
{
    private string? _armedTag;
    private Func<CancellationToken, Task>? _armedAction;

    public void ArmBeforeNext(string tag, Func<CancellationToken, Task> action)
    {
        _armedTag = tag;
        _armedAction = action;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        var tag = _armedTag;
        var action = _armedAction;
        if (tag is not null && action is not null && command.CommandText.Contains(tag, StringComparison.Ordinal))
        {
            // One-shot: clear before invoking so the action's own queries (issued through a
            // different AppDbContext/connection, but the same interceptor instance) can never
            // re-trigger this or a later, unrelated tagged query.
            _armedTag = null;
            _armedAction = null;
            await action(cancellationToken);
        }

        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
