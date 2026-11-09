using BirraPoint.Api.Common.Persistence;

namespace BirraPoint.Api.UnitTests.Common.Persistence;

/// <summary>Startup refuses a database built from the pre-baseline migrations and names the reset command
/// (FR-070); empty and baseline databases proceed.</summary>
public sealed class LegacyDatabaseGuardTests
{
    private static readonly string[] Known = ["20261108000000_InitialCreate"];

    [Fact]
    public void OldPublicHistoryIsRefused()
    {
        var problem = LegacyDatabaseGuard.Evaluate(legacyHistoryExists: true, applied: [], known: Known);

        Assert.NotNull(problem);
        Assert.Contains("Reset database", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownAppliedMigrationIsRefused()
    {
        var problem = LegacyDatabaseGuard.Evaluate(
            legacyHistoryExists: false, applied: ["20260708211914_InitialCreate"], known: Known);

        Assert.NotNull(problem);
        Assert.Contains("Reset database", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyDatabaseProceeds() =>
        Assert.Null(LegacyDatabaseGuard.Evaluate(legacyHistoryExists: false, applied: [], known: Known));

    [Fact]
    public void BaselineOnlyDatabaseProceeds() =>
        Assert.Null(LegacyDatabaseGuard.Evaluate(legacyHistoryExists: false, applied: Known, known: Known));
}
