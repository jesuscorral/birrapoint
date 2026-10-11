using BirraPoint.Api.Features.Judges;

namespace BirraPoint.Api.UnitTests.Judges;

/// <summary>
/// T038: bulk registration dedup — in-list duplicates (case-insensitive, first occurrence wins)
/// and already-registered emails, each with the correct skip reason (FR-014/FR-015).
/// </summary>
public sealed class JudgeRegistrationPlannerTests
{
    [Fact]
    public void UniqueEmailsAreAllPlannedForCreation()
    {
        var plan = JudgeRegistrationPlanner.Plan(
            ["alice@example.com", "bob@example.com"], existingEmails: EmptySet());

        Assert.Equal(["alice@example.com", "bob@example.com"], plan.ToCreate);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void DuplicateInListIsSkippedWithDuplicateInListReason()
    {
        var plan = JudgeRegistrationPlanner.Plan(
            ["alice@example.com", "alice@example.com"], existingEmails: EmptySet());

        Assert.Equal(["alice@example.com"], plan.ToCreate);
        var skip = Assert.Single(plan.Skipped);
        Assert.Equal("alice@example.com", skip.Email);
        Assert.Equal("duplicate-in-list", skip.Reason);
    }

    [Fact]
    public void DuplicateInListDetectionIsCaseInsensitive()
    {
        var plan = JudgeRegistrationPlanner.Plan(
            ["Alice@Example.com", "alice@example.com"], existingEmails: EmptySet());

        Assert.Equal(["Alice@Example.com"], plan.ToCreate);
        var skip = Assert.Single(plan.Skipped);
        Assert.Equal("alice@example.com", skip.Email);
        Assert.Equal("duplicate-in-list", skip.Reason);
    }

    [Fact]
    public void FirstOccurrenceOfADuplicateWinsAndKeepsItsOriginalCasing()
    {
        var plan = JudgeRegistrationPlanner.Plan(
            ["Alice@Example.com", "ALICE@EXAMPLE.COM", "alice@example.com"], existingEmails: EmptySet());

        Assert.Equal(["Alice@Example.com"], plan.ToCreate);
        Assert.Equal(2, plan.Skipped.Count);
        Assert.All(plan.Skipped, skip => Assert.Equal("duplicate-in-list", skip.Reason));
    }

    [Fact]
    public void AlreadyRegisteredEmailIsSkippedWithAlreadyRegisteredReason()
    {
        var plan = JudgeRegistrationPlanner.Plan(
            ["bob@example.com"], existingEmails: new HashSet<string>(["bob@example.com"], StringComparer.OrdinalIgnoreCase));

        Assert.Empty(plan.ToCreate);
        var skip = Assert.Single(plan.Skipped);
        Assert.Equal("bob@example.com", skip.Email);
        Assert.Equal("already-registered", skip.Reason);
    }

    [Fact]
    public void AlreadyRegisteredDetectionIsCaseInsensitive()
    {
        var plan = JudgeRegistrationPlanner.Plan(
            ["Bob@Example.com"], existingEmails: new HashSet<string>(["bob@example.com"], StringComparer.OrdinalIgnoreCase));

        Assert.Empty(plan.ToCreate);
        var skip = Assert.Single(plan.Skipped);
        Assert.Equal("Bob@Example.com", skip.Email);
        Assert.Equal("already-registered", skip.Reason);
    }

    [Fact]
    public void InListDuplicateTakesPriorityOverAlreadyRegisteredForLaterOccurrences()
    {
        var plan = JudgeRegistrationPlanner.Plan(
            ["carol@example.com", "carol@example.com"],
            existingEmails: new HashSet<string>(["carol@example.com"], StringComparer.OrdinalIgnoreCase));

        Assert.Empty(plan.ToCreate);
        Assert.Equal(2, plan.Skipped.Count);
        Assert.Equal("already-registered", plan.Skipped[0].Reason);
        Assert.Equal("duplicate-in-list", plan.Skipped[1].Reason);
    }

    private static HashSet<string> EmptySet() => new(StringComparer.OrdinalIgnoreCase);
}
