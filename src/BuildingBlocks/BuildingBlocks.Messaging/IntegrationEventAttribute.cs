namespace BuildingBlocks.Messaging;

/// <summary>
/// Declares an integration event: a stable string <see cref="Type"/> (<c>&lt;module&gt;.&lt;kebab-name&gt;</c>)
/// and a <see cref="Version"/>. Storage and transports use these, never the CLR name, so the class can be
/// renamed or moved without breaking events already in an outbox. Additive payload changes keep the version;
/// a breaking change registers a new version side by side.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class IntegrationEventAttribute : Attribute
{
    public const int TypeMaxLength = 200;

    public IntegrationEventAttribute(string type, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        if (type.Length > TypeMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(type), $"The event type must be at most {TypeMaxLength} characters.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        Type = type;
        Version = version;
    }

    public string Type { get; }

    public int Version { get; }
}
