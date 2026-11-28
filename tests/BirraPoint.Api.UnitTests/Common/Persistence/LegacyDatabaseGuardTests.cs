using BirraPoint.Api.Common.Persistence;

namespace BirraPoint.Api.UnitTests.Common.Persistence;

/// <summary>Startup refuses a database built from the pre-baseline migrations and names the reset command
/// (FR-070) or from the shared history in the migrations schema (FR-098); empty and baseline databases proceed.</summary>
public sealed class LegacyDatabaseGuardTests
{
    private static readonly string[] Known = ["20261108000000_InitialCreate"];

    [Fact]
    public void OldPublicHistoryIsRefused()
    {
        var problem = LegacyDatabaseGuard.Evaluate(publicHistoryExists: true, sharedMigrationsHistoryExists: false, applied: [], known: Known);

        Assert.NotNull(problem);
        Assert.Contains("Reset database", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedHistoryInTheMigrationsSchemaIsRefusedNamingTheResetCommand()
    {
        var problem = LegacyDatabaseGuard.Evaluate(
            publicHistoryExists: false, sharedMigrationsHistoryExists: true, applied: [], known: Known);

        Assert.NotNull(problem);
        Assert.Contains("Reset database", problem, StringComparison.Ordinal);
        Assert.Contains("migrations", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicHistoryMessageStillNamesThePublicSchema()
    {
        var problem = LegacyDatabaseGuard.Evaluate(
            publicHistoryExists: true, sharedMigrationsHistoryExists: false, applied: [], known: Known);

        Assert.Contains("public", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownAppliedMigrationIsRefused()
    {
        var problem = LegacyDatabaseGuard.Evaluate(
            publicHistoryExists: false, sharedMigrationsHistoryExists: false, applied: ["20260708211914_InitialCreate"], known: Known);

        Assert.NotNull(problem);
        Assert.Contains("Reset database", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownMigrationNewerThanTheBaselineProceeds() =>
        Assert.Null(LegacyDatabaseGuard.Evaluate(
            publicHistoryExists: false, sharedMigrationsHistoryExists: false,
            applied: [.. Known, "20261109120000_AddSomethingLater"],
            known: Known));

    [Fact]
    public void UnknownMigrationOlderThanTheBaselineIsRefusedEvenNextToKnownOnes()
    {
        var problem = LegacyDatabaseGuard.Evaluate(
            publicHistoryExists: false, sharedMigrationsHistoryExists: false,
            applied: [.. Known, "20260708211914_InitialCreate"],
            known: Known);

        Assert.NotNull(problem);
    }

    [Fact]
    public void EmptyDatabaseProceeds() =>
        Assert.Null(LegacyDatabaseGuard.Evaluate(publicHistoryExists: false, sharedMigrationsHistoryExists: false, applied: [], known: Known));

    [Fact]
    public void BaselineOnlyDatabaseProceeds() =>
        Assert.Null(LegacyDatabaseGuard.Evaluate(publicHistoryExists: false, sharedMigrationsHistoryExists: false, applied: Known, known: Known));
}
