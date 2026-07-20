using BirraPoint.Api.Common.Auth;
using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Judges;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.UnitTests.Judges;

/// <summary>
/// T038: email correction format validation (FR-015). The uniqueness re-check is a DependentRules
/// step that only runs once the format passes, so it is never reached here — no query executes
/// against <see cref="AppDbContext"/>, meaning these malformed-input cases don't need a real
/// database (the uniqueness-conflict path itself needs Postgres and is covered by T039's
/// Testcontainers-backed integration tests, per the constitution's ban on EF InMemory).
/// </summary>
public sealed class UpdateJudgeEmailCommandValidatorTests
{
    // Never queried (DependentRules short-circuits before the MustAsync uniqueness check runs for
    // a malformed email) — configured with the project's real Npgsql provider so no new test-only
    // dependency (e.g. EF InMemory) is introduced, and no connection is ever opened.
    private static AppDbContext CreateUnusedDbContext() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=unused;Database=unused").Options);

    // Never called either, for the same reason — only the constructor shape needs satisfying.
    private sealed class UnusedCurrentUser : ICurrentUser
    {
        public string Sub => throw new InvalidOperationException("Not expected to be called.");
        public string? Email => throw new InvalidOperationException("Not expected to be called.");
        public string? Name => throw new InvalidOperationException("Not expected to be called.");
        public IReadOnlyList<string> Roles => throw new InvalidOperationException("Not expected to be called.");
        public Task<IReadOnlyList<Judge>> GetJudgeRecordsAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Not expected to be called.");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public void Command_with_malformed_email_is_invalid(string email)
    {
        using var dbContext = CreateUnusedDbContext();
        var validator = new UpdateJudgeEmailCommandValidator(dbContext, new UnusedCurrentUser());

        var result = validator.Validate(new UpdateJudgeEmailCommand(Guid.NewGuid(), Guid.NewGuid(), email));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateJudgeEmailCommand.Email));
    }
}
