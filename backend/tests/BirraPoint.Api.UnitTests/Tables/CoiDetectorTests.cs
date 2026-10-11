using BirraPoint.Api.Features.Tables;

namespace BirraPoint.Api.UnitTests.Tables;

/// <summary>
/// T045: pure COI classification (FR-017) — a judge conflicts with a beer entry when the judge's
/// email matches the entry's owner or any collaborator, case-insensitively.
/// </summary>
public sealed class CoiDetectorTests
{
    [Fact]
    public void NoConflictWhenNoEmailMatches()
    {
        var judgeId = Guid.NewGuid();
        var entryId = Guid.NewGuid();

        var conflicts = CoiDetector.FindConflicts(
            judgeEmailsById: new Dictionary<Guid, string> { [judgeId] = "judge@example.com" },
            beerEntryIds: [entryId],
            entryOwnerEmailsById: new Dictionary<Guid, IReadOnlySet<string>>
            {
                [entryId] = Set("owner@example.com"),
            });

        Assert.Empty(conflicts);
    }

    [Fact]
    public void ConflictWhenJudgeEmailMatchesEntryOwner()
    {
        var judgeId = Guid.NewGuid();
        var entryId = Guid.NewGuid();

        var conflicts = CoiDetector.FindConflicts(
            judgeEmailsById: new Dictionary<Guid, string> { [judgeId] = "owner@example.com" },
            beerEntryIds: [entryId],
            entryOwnerEmailsById: new Dictionary<Guid, IReadOnlySet<string>>
            {
                [entryId] = Set("owner@example.com"),
            });

        var conflict = Assert.Single(conflicts);
        Assert.Equal(judgeId, conflict.JudgeId);
        Assert.Equal([entryId], conflict.BeerEntryIds);
    }

    [Fact]
    public void ConflictWhenJudgeEmailMatchesACollaborator()
    {
        var judgeId = Guid.NewGuid();
        var entryId = Guid.NewGuid();

        var conflicts = CoiDetector.FindConflicts(
            judgeEmailsById: new Dictionary<Guid, string> { [judgeId] = "collaborator@example.com" },
            beerEntryIds: [entryId],
            entryOwnerEmailsById: new Dictionary<Guid, IReadOnlySet<string>>
            {
                [entryId] = Set("owner@example.com", "collaborator@example.com"),
            });

        var conflict = Assert.Single(conflicts);
        Assert.Equal(judgeId, conflict.JudgeId);
        Assert.Equal([entryId], conflict.BeerEntryIds);
    }

    [Fact]
    public void MatchingIsCaseInsensitive()
    {
        var judgeId = Guid.NewGuid();
        var entryId = Guid.NewGuid();

        var conflicts = CoiDetector.FindConflicts(
            judgeEmailsById: new Dictionary<Guid, string> { [judgeId] = "Owner@Example.com" },
            beerEntryIds: [entryId],
            entryOwnerEmailsById: new Dictionary<Guid, IReadOnlySet<string>>
            {
                [entryId] = Set("owner@example.com"),
            });

        Assert.Single(conflicts);
    }

    [Fact]
    public void ConflictsAreGroupedPerJudgeAcrossMultipleEntries()
    {
        var judgeId = Guid.NewGuid();
        var ownedEntryId = Guid.NewGuid();
        var otherEntryId = Guid.NewGuid();

        var conflicts = CoiDetector.FindConflicts(
            judgeEmailsById: new Dictionary<Guid, string> { [judgeId] = "owner@example.com" },
            beerEntryIds: [ownedEntryId, otherEntryId],
            entryOwnerEmailsById: new Dictionary<Guid, IReadOnlySet<string>>
            {
                [ownedEntryId] = Set("owner@example.com"),
                [otherEntryId] = Set("someone-else@example.com"),
            });

        var conflict = Assert.Single(conflicts);
        Assert.Equal(judgeId, conflict.JudgeId);
        Assert.Equal([ownedEntryId], conflict.BeerEntryIds);
    }

    [Fact]
    public void MultipleJudgesCanEachProduceTheirOwnConflict()
    {
        var judgeAId = Guid.NewGuid();
        var judgeBId = Guid.NewGuid();
        var entryAId = Guid.NewGuid();
        var entryBId = Guid.NewGuid();

        var conflicts = CoiDetector.FindConflicts(
            judgeEmailsById: new Dictionary<Guid, string>
            {
                [judgeAId] = "a@example.com",
                [judgeBId] = "b@example.com",
            },
            beerEntryIds: [entryAId, entryBId],
            entryOwnerEmailsById: new Dictionary<Guid, IReadOnlySet<string>>
            {
                [entryAId] = Set("a@example.com"),
                [entryBId] = Set("b@example.com"),
            });

        Assert.Equal(2, conflicts.Count);
        Assert.Contains(conflicts, c => c.JudgeId == judgeAId && c.BeerEntryIds.SequenceEqual([entryAId]));
        Assert.Contains(conflicts, c => c.JudgeId == judgeBId && c.BeerEntryIds.SequenceEqual([entryBId]));
    }

    private static HashSet<string> Set(params string[] emails) =>
        new HashSet<string>(emails, StringComparer.OrdinalIgnoreCase);
}
