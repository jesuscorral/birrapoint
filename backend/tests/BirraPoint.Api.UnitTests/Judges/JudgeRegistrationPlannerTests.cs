using BirraPoint.Api.Features.Judges;

namespace BirraPoint.Api.UnitTests.Judges;

/// <summary>
/// T038: bulk registration dedup — in-list duplicates (case-insensitive, first occurrence wins)
/// and already-registered emails, each with the correct skip reason (FR-014/FR-015).
/// </summary>
public sealed class JudgeRegistrationPlannerTests
{
    [Fact]
    public void Unique_emails_are_all_planned_for_creation()
    {
        var plan = JudgeRegistrationPlanner.Plan(
            ["alice@example.com", "bob@example.com"], existingEmails: EmptySet());

        Assert.Equal(["alice@example.com", "bob@example.com"], plan.ToCreate);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Duplicate_in_list_is_skipped_with_duplicate_in_list_reason()
    {
        var plan = JudgeRegistrationPlanner.Plan(
            ["alice@example.com", "alice@example.com"], existingEmails: EmptySet());

        Assert.Equal(["alice@example.com"], plan.ToCreate);
        var skip = Assert.Single(plan.Skipped);
        Assert.Equal("alice@example.com", skip.Email);
        Assert.Equal("duplicate-in-list", skip.Reason);
    }

    [Fact]
    public void Duplicate_in_list_detection_is_case_insensitive()
    {
        var plan = JudgeRegistrationPlanner.Plan(
            ["Alice@Example.com", "alice@example.com"], existingEmails: EmptySet());

        Assert.Equal(["Alice@Example.com"], plan.ToCreate);
        var skip = Assert.Single(plan.Skipped);
        Assert.Equal("alice@example.com", skip.Email);
        Assert.Equal("duplicate-in-list", skip.Reason);
    }

    [Fact]
    public void First_occurrence_of_a_duplicate_wins_and_keeps_its_original_casing()
    {
        var plan = JudgeRegistrationPlanner.Plan(
            ["Alice@Example.com", "ALICE@EXAMPLE.COM", "alice@example.com"], existingEmails: EmptySet());

        Assert.Equal(["Alice@Example.com"], plan.ToCreate);
        Assert.Equal(2, plan.Skipped.Count);
        Assert.All(plan.Skipped, skip => Assert.Equal("duplicate-in-list", skip.Reason));
    }

    [Fact]
    public void Already_registered_email_is_skipped_with_already_registered_reason()
    {
        var plan = JudgeRegistrationPlanner.Plan(
            ["bob@example.com"], existingEmails: new HashSet<string>(["bob@example.com"], StringComparer.OrdinalIgnoreCase));

        Assert.Empty(plan.ToCreate);
        var skip = Assert.Single(plan.Skipped);
        Assert.Equal("bob@example.com", skip.Email);
        Assert.Equal("already-registered", skip.Reason);
    }

    [Fact]
    public void Already_registered_detection_is_case_insensitive()
    {
        var plan = JudgeRegistrationPlanner.Plan(
            ["Bob@Example.com"], existingEmails: new HashSet<string>(["bob@example.com"], StringComparer.OrdinalIgnoreCase));

        Assert.Empty(plan.ToCreate);
        var skip = Assert.Single(plan.Skipped);
        Assert.Equal("Bob@Example.com", skip.Email);
        Assert.Equal("already-registered", skip.Reason);
    }

    [Fact]
    public void In_list_duplicate_takes_priority_over_already_registered_for_later_occurrences()
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
