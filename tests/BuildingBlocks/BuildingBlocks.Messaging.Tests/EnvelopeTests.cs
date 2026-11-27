#pragma warning disable CA1812 // Event classes below exist only as type arguments; they are never instantiated.

namespace BuildingBlocks.Messaging.Tests;

/// <summary>Event declaration, envelope serialization and registration rules (FR-076).</summary>
public sealed class EnvelopeTests
{
    private sealed record NotAnnotated(Guid Id);

    [IntegrationEvent("test.same-name", 1)]
    private sealed record Twin1(Guid Id);

    [IntegrationEvent("test.same-name", 1)]
    private sealed record Twin2(Guid Id);

    [IntegrationEvent("test.same-name", 2)]
    private sealed record TwinV2(Guid Id);

    [Fact]
    public void EventSerializesToTypeVersionAndCamelCasePayloadAndBack()
    {
        var descriptor = EventDescriptor.For<ThingHappened>();
        var evt = new ThingHappened(Guid.NewGuid());

        var envelope = EventSerializer.ToEnvelope(descriptor, evt, Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        Assert.Equal("test.thing-happened", envelope.Type);
        Assert.Equal(1, envelope.Version);
        Assert.Equal("{\"thingId\":\"" + evt.ThingId + "\"}", envelope.PayloadJson);
        Assert.Equal(evt, EventSerializer.Deserialize(descriptor, envelope.PayloadJson));
    }

    [Fact]
    public void EventWithoutTheAttributeIsRejectedAtRegistration()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new MessagingBuilder().Publishes<NotAnnotated>("Orders"));

        Assert.Contains(nameof(IntegrationEventAttribute), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoEventsWithTheSameTypeAndVersionAreRejected()
    {
        var builder = new MessagingBuilder();
        builder.Publishes<Twin1>("Orders");

        Assert.Throws<InvalidOperationException>(() => builder.Publishes<Twin2>("Billing"));
    }

    [Fact]
    public void TheSameTypeInAnotherVersionIsAccepted()
    {
        var builder = new MessagingBuilder();
        builder.Publishes<Twin1>("Orders");

        Assert.Null(Record.Exception(() => builder.Publishes<TwinV2>("Orders")));
    }

    [Fact]
    public void AnEventHasOneOutboxOnly()
    {
        var builder = new MessagingBuilder();
        builder.Publishes<ThingHappened>("Orders");

        Assert.Throws<InvalidOperationException>(() => builder.Publishes<ThingHappened>("Billing"));
    }

    [Fact]
    public void VersionBelowOneIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntegrationEventAttribute("x.y", 0));
}
