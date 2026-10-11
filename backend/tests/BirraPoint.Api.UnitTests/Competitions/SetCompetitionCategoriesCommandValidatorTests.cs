using BirraPoint.Api.Common.Persistence;
using BirraPoint.Api.Features.Competitions;
using Microsoft.EntityFrameworkCore;

namespace BirraPoint.Api.UnitTests.Competitions;

/// <summary>
/// SetCompetitionCategoriesCommandValidator's sync rules (empty categories, zero total styles,
/// duplicate category name, duplicate style code across categories). The rule chain uses
/// Cascade(Stop) so the DB-backed style-existence MustAsync check in DependentRules never runs
/// once one of these sync checks has already failed — same "never queried" shape as
/// UpdateJudgeEmailCommandValidatorTests, so no real database is needed here. The happy-path
/// (existing style codes) and unknown-style-code cases require real BjcpStyle rows and are
/// covered by CompetitionCategoriesApiTests (Testcontainers) instead, mirroring how
/// CreateTableCommandValidator/ResolveRowCommandValidator's own DB-backed rules have no unit test
/// coverage in this codebase — only their HTTP-level integration tests.
/// </summary>
public sealed class SetCompetitionCategoriesCommandValidatorTests
{
    // Never queried — configured with the project's real Npgsql provider so no new test-only
    // dependency (e.g. EF InMemory) is introduced, and no connection is ever opened.
    private static readonly AppDbContext UnusedDbContext = new(
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=unused;Database=unused").Options);

    private static SetCompetitionCategoriesCommandValidator CreateValidator() => new(UnusedDbContext);

    [Fact]
    public void CommandWithNoCategoriesIsInvalid()
    {
        var validator = CreateValidator();

        var result = validator.Validate(new SetCompetitionCategoriesCommand(Guid.NewGuid(), []));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetCompetitionCategoriesCommand.Categories));
    }

    [Fact]
    public void CommandWhereNoCategoryHasAnyStyleIsInvalid()
    {
        var validator = CreateValidator();
        var command = new SetCompetitionCategoriesCommand(
            Guid.NewGuid(),
            [new SetCategoryItem("Classic Styles", 0, []), new SetCategoryItem("Modern Styles", 1, [])]);

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetCompetitionCategoriesCommand.Categories));
    }

    [Fact]
    public void CommandWithDuplicateCategoryNamesIsInvalid()
    {
        var validator = CreateValidator();
        var command = new SetCompetitionCategoriesCommand(
            Guid.NewGuid(),
            [
                new SetCategoryItem("Classic Styles", 0, ["21A"]),
                new SetCategoryItem("Classic Styles", 1, ["1A"]),
            ]);

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetCompetitionCategoriesCommand.Categories));
    }

    [Fact]
    public void CommandWithTheSameStyleCodeInTwoCategoriesIsInvalid()
    {
        var validator = CreateValidator();
        var command = new SetCompetitionCategoriesCommand(
            Guid.NewGuid(),
            [
                new SetCategoryItem("Classic Styles", 0, ["21A"]),
                new SetCategoryItem("Modern Styles", 1, ["21A"]),
            ]);

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SetCompetitionCategoriesCommand.Categories));
    }
}
