using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Competitions.Features.CreateCompetition;

namespace BirraPoint.Api.UnitTests.Competitions.Features.CreateCompetition;

/// <summary>T025: CreateCompetitionCommandValidator — required fields, endDate &gt;= startDate, registration window, entry limit &gt; 0.</summary>
public sealed class CreateCompetitionCommandValidatorTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);

    private static CreateCompetitionCommand ValidCreateCommand() => new(
        Name: "Copa BirraPoint",
        Venue: "Centro de Convenciones",
        StartDate: Today,
        EndDate: Today.AddDays(2),
        Description: null,
        LogoUrl: null,
        EntryLimit: null,
        RegistrationStart: null,
        RegistrationEnd: null);

    [Fact]
    public void CreateCommandWithAllRequiredFieldsIsValid()
    {
        var result = new CreateCompetitionCommandValidator().Validate(ValidCreateCommand());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateCommandRequiresName()
    {
        var command = ValidCreateCommand() with { Name = "" };

        var result = new CreateCompetitionCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompetitionCommand.Name));
    }

    [Fact]
    public void CreateCommandRequiresVenue()
    {
        var command = ValidCreateCommand() with { Venue = "" };

        var result = new CreateCompetitionCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompetitionCommand.Venue));
    }

    [Fact]
    public void CreateCommandRejectsEndDateBeforeStartDate()
    {
        var command = ValidCreateCommand() with { StartDate = Today, EndDate = Today.AddDays(-1) };

        var result = new CreateCompetitionCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompetitionCommand.EndDate));
    }

    [Fact]
    public void CreateCommandRejectsNonPositiveEntryLimit()
    {
        var command = ValidCreateCommand() with { EntryLimit = 0 };

        var result = new CreateCompetitionCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompetitionCommand.EntryLimit));
    }

    [Fact]
    public void CreateCommandRejectsRegistrationEndBeforeRegistrationStart()
    {
        var command = ValidCreateCommand() with { RegistrationStart = Today, RegistrationEnd = Today.AddDays(-1) };

        var result = new CreateCompetitionCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateCompetitionCommand.RegistrationEnd));
    }

    [Fact]
    public void CreateCommandAllowsRegistrationWindowWhenOnlyOneBoundIsSet()
    {
        var command = ValidCreateCommand() with { RegistrationStart = Today };

        var result = new CreateCompetitionCommandValidator().Validate(command);

        Assert.True(result.IsValid);
    }
}
