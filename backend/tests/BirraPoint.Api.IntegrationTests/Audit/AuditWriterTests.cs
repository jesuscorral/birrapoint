using System.Text.Json;
using BirraPoint.Api.Common.Audit;
using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.IntegrationTests.Audit;

/// <summary>
/// T014: AuditWriter stages an AuditLog row via the DbContext's change tracker without calling
/// SaveChanges itself, so it commits atomically with whatever business change the caller persists.
/// </summary>
public sealed class AuditWriterTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private sealed class FakeCurrentUser(string sub) : ICurrentUser
    {
        public string Sub { get; } = sub;
        public string? Email => null;
        public string? Name => null;
        public IReadOnlyList<string> Roles => [];
        public Task<IReadOnlyList<Judge>> GetJudgeRecordsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Judge>>([]);
    }

    private AppDbContext NewContext() => new(fixture.Options);

    [Fact]
    public async Task Record_stages_an_audit_log_row_that_persists_on_the_callers_save_changes()
    {
        await using var db = NewContext();
        var writer = new AuditWriter(db, new FakeCurrentUser("kc-organizer-42"));
        var entryId = Guid.NewGuid();
        var before = new Dictionary<string, object?> { ["Total"] = 30 };
        var after = new Dictionary<string, object?> { ["Total"] = 35 };

        writer.Record("EvaluationCorrected", "Evaluation", entryId.ToString(), before, after);
        await db.SaveChangesAsync();

        await using var verify = NewContext();
        var stored = await verify.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == entryId.ToString());

        Assert.Equal("kc-organizer-42", stored.ActorUserId);
        Assert.Equal("EvaluationCorrected", stored.Action);
        Assert.Equal("Evaluation", stored.EntityType);
        Assert.InRange(stored.OccurredAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));

        using var data = JsonDocument.Parse(stored.DataJson);
        Assert.Equal(30, data.RootElement.GetProperty("before").GetProperty("Total").GetInt32());
        Assert.Equal(35, data.RootElement.GetProperty("after").GetProperty("Total").GetInt32());
    }

    [Fact]
    public async Task Record_accepts_a_null_before_for_creation_style_actions()
    {
        await using var db = NewContext();
        var writer = new AuditWriter(db, new FakeCurrentUser("kc-organizer-1"));
        var entryId = Guid.NewGuid();

        writer.Record("JudgeInvited", "Judge", entryId.ToString(), before: null, after: new { Email = "judge@example.test" });
        await db.SaveChangesAsync();

        await using var verify = NewContext();
        var stored = await verify.AuditLogs.AsNoTracking().SingleAsync(a => a.EntityId == entryId.ToString());

        using var data = JsonDocument.Parse(stored.DataJson);
        Assert.Equal(JsonValueKind.Null, data.RootElement.GetProperty("before").ValueKind);
        Assert.Equal("judge@example.test", data.RootElement.GetProperty("after").GetProperty("Email").GetString());
    }

    [Fact]
    public async Task Does_not_persist_when_the_caller_never_calls_save_changes()
    {
        await using var db = NewContext();
        var writer = new AuditWriter(db, new FakeCurrentUser("kc-organizer-1"));
        var entryId = Guid.NewGuid();

        writer.Record("TableClosed", "TastingTable", entryId.ToString());
        // Intentionally no SaveChangesAsync call.

        await using var verify = NewContext();
        var exists = await verify.AuditLogs.AsNoTracking().AnyAsync(a => a.EntityId == entryId.ToString());

        Assert.False(exists);
    }
}
