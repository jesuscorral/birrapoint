using BirraPoint.Api.Domain;
using BirraPoint.Api.Features.Competitions.Features.UpdateCompetition;

namespace BirraPoint.Api.UnitTests.Competitions.Features.UpdateCompetition;

/// <summary>UpdateCompetitionCommandValidator — same field rules as create.</summary>
public sealed class UpdateCompetitionCommandValidatorTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);

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
}
