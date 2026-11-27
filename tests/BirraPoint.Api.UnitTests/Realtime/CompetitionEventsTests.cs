using System.Reflection;
using BirraPoint.Api.Realtime;

namespace BirraPoint.Api.UnitTests.Realtime;

/// <summary>The event names are the closed list of the realtime contract; a constant's value is its name.</summary>
public sealed class CompetitionEventsTests
{
    [Fact]
    public void EveryConstantValueEqualsItsName()
    {
        var constants = typeof(CompetitionEvents).GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral)
            .ToList();

        Assert.Contains(constants, f => f.Name == nameof(CompetitionEvents.JudgeConflictDetected));
        Assert.All(constants, f => Assert.Equal(f.Name, (string?)f.GetRawConstantValue()));
    }
}
