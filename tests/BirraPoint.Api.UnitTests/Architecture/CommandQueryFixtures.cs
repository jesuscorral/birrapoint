using System.Diagnostics.CodeAnalysis;
using BirraPoint.Common.Modules;
using MediatR;

namespace BirraPoint.Api.UnitTests.Architecture;

// Types the negative tests of the command/query rules inspect by reflection; none is ever instantiated.
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Inspected by reflection only.")]
internal sealed record PlainRequest : IRequest<int>;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Inspected by reflection only.")]
internal sealed record FixtureCommand : ICommand<int>;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Inspected by reflection only.")]
internal sealed record FixtureQuery : IQuery<int>;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Inspected by reflection only.")]
internal sealed record BothMarkers : ICommand<int>, IQuery<int>;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Inspected by reflection only.")]
internal sealed class FixtureAggregate
{
    public int Total { get; private set; }

    public string Name { get; set; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public void Bump() => Total++;
}
