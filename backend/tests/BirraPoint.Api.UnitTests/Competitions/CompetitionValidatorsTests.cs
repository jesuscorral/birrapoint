using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Competitions;

namespace BirraPoint.Api.UnitTests.Competitions;

/// <summary>
/// T025: CreateCompetition/UpdateCompetition validators (required fields, endDate >= startDate,
/// registration window, entry limit > 0) and CompetitionStateMachine gates (forward-only,
/// skip-free, reverse/same-state rejected) per FR-006.
/// </summary>
public sealed class CompetitionValidatorsTests
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

    private static UpdateCompetitionCommand ValidUpdateCommand() => new(
        Id: Guid.NewGuid(),
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
    public void UpdateCommandWithAllRequiredFieldsIsValid()
    {
        var result = new UpdateCompetitionCommandValidator().Validate(ValidUpdateCommand());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateCommandRequiresName()
    {
        var command = ValidUpdateCommand() with { Name = "" };

        var result = new UpdateCompetitionCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompetitionCommand.Name));
    }

    [Fact]
    public void UpdateCommandRejectsEndDateBeforeStartDate()
    {
        var command = ValidUpdateCommand() with { StartDate = Today, EndDate = Today.AddDays(-1) };

        var result = new UpdateCompetitionCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompetitionCommand.EndDate));
    }

    [Fact]
    public void UpdateCommandRejectsRegistrationEndBeforeRegistrationStart()
    {
        var command = ValidUpdateCommand() with { RegistrationStart = Today, RegistrationEnd = Today.AddDays(-1) };

        var result = new UpdateCompetitionCommandValidator().Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateCompetitionCommand.RegistrationEnd));
    }

    [Theory]
    [InlineData(CompetitionState.Draft, CompetitionState.Active, true)]
    [InlineData(CompetitionState.Active, CompetitionState.InEvaluation, true)]
    [InlineData(CompetitionState.InEvaluation, CompetitionState.Finalized, true)]
    [InlineData(CompetitionState.Draft, CompetitionState.InEvaluation, false)]
    [InlineData(CompetitionState.Draft, CompetitionState.Finalized, false)]
    [InlineData(CompetitionState.Active, CompetitionState.Finalized, false)]
    [InlineData(CompetitionState.Active, CompetitionState.Draft, false)]
    [InlineData(CompetitionState.InEvaluation, CompetitionState.Draft, false)]
    [InlineData(CompetitionState.InEvaluation, CompetitionState.Active, false)]
    [InlineData(CompetitionState.Finalized, CompetitionState.Active, false)]
    [InlineData(CompetitionState.Draft, CompetitionState.Draft, false)]
    [InlineData(CompetitionState.Finalized, CompetitionState.Finalized, false)]
    public void StateMachineAllowsOnlyForwardSkipFreeTransitions(
        CompetitionState from, CompetitionState to, bool expected)
    {
        Assert.Equal(expected, CompetitionStateMachine.CanTransition(from, to));
    }
}
