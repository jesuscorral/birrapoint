namespace BuildingBlocks.Validation.MediatR;

/// <summary>The host-owned identity of the validation problem (its type URI and title).</summary>
public sealed class ValidationProblemOptions
{
    /// <summary>The problem <c>type</c> URI written for a validation failure.</summary>
    public string? Type { get; set; }

    /// <summary>The problem <c>title</c> written for a validation failure.</summary>
    public string? Title { get; set; }
}
