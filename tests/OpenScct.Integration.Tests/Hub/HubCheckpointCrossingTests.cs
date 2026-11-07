using OpenScct.Core;
using OpenScct.Server.Hub;
using Xunit;

namespace OpenScct.Integration.Tests.Hub;

public sealed class HubCheckpointCrossingTests
{
    private static readonly HubGate Gate = new("Checkpoint", default, 96, 24, 160);

    [Fact]
    public void RunningAcrossBetweenSamplesRegistersOnTheFirstPass()
    {
        var from = new FVector(-200, 0, 89);
        var to = new FVector(200, 0, 89);
        Assert.False(Gate.Contains(from));
        Assert.False(Gate.Contains(to));
        Assert.True(Gate.Crosses(from, to, 35, 87.5f, out float entry));
        Assert.InRange(entry, .17f, .18f);
    }

    [Theory]
    [InlineData(89, 87.5f, true)] // Running.
    [InlineData(46, 43.75f, true)] // Rolling/crouched.
    [InlineData(220, 87.5f, true)] // Jumping across the painted disc.
    [InlineData(300, 87.5f, false)] // A separate upper route.
    [InlineData(-40, 87.5f, false)] // Beneath the deck.
    public void FootHeightAcceptsRunningRollingAndLowJumpsButNotOtherFloors(float z, float halfHeight, bool expected)
        => Assert.Equal(expected, Gate.Crosses(new(-200, 0, z), new(200, 0, z), 35, halfHeight, out _));

    [Theory]
    [InlineData(125, true)] // The runner's body clips the disc edge.
    [InlineData(140, false)]
    public void BodyWidthCountsAtTheCircularEdge(float y, bool expected)
        => Assert.Equal(expected, Gate.Crosses(new(-200, y, 89), new(200, y, 89), 35, 87.5f, out _));

    [Fact]
    public void HorizontalAndVerticalContactMustHappenAtTheSameTime()
        => Assert.False(Gate.Crosses(new(-400, 0, 89), new(0, 0, 1000), 35, 87.5f, out _));

    private static HubCourseDefinition Course(params HubGate[] gates) => new("test", "Test", 1, Gate, Gate,
        (-1000, -1000, 1000, 1000), gates, [], [], default, HubSignFace.None, 300);

    [Fact]
    public void OneMovementCanEarnConsecutiveGatesInTravelOrder()
    {
        var course = Course(new("First", new(-80, 0, 0), 20), new("Second", new(80, 0, 0), 20));
        Assert.Equal(2, course.AdvanceGates(0, new(-200, 0, 89), new(200, 0, 89), 35, 87.5f, out _));
    }

    [Fact]
    public void CrossingLaterGateBeforeTheRequiredGateDoesNotEarnIt()
    {
        var course = Course(new("First", new(80, 0, 0), 20), new("Second", new(-80, 0, 0), 20));
        Assert.Equal(1, course.AdvanceGates(0, new(-200, 0, 89), new(200, 0, 89), 35, 87.5f, out _));
    }
}
