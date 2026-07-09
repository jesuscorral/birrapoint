namespace BirraPoint.Api.Domain;

/// <summary>Closed is terminal (FR-033); judge mutations are rejected afterwards (FR-034).</summary>
public enum TableState
{
    Open,
    Closed,
}
