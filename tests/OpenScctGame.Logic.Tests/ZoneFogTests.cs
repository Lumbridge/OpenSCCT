using System.Numerics;
using OpenScctGame.Render.Post;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class ZoneFogTests
{
    private static FogPortal Portal(float x, int back = 0, int front = 1) => new(new(1, 0, 0, x), back, front,
        [new(x, -10, -10), new(x, 10, -10), new(x, 10, 10), new(x, -10, 10)]);

    [Fact]
    public void FoggyRoomSeenFromClearRoomUsesOnlyItsSegment()
    {
        FogZone[] zones = [new(0, 100, Vector3.Zero, false), new(0, 100, Vector3.UnitX, true)];
        Assert.Equal(new Vector4(.3f, 0, 0, .7f), ZoneFogRules.Evaluate(zones, [Portal(20)], 0, Vector3.Zero, new(50, 0, 0)));
    }

    [Fact]
    public void SplittingIdenticalRoomsDoesNotChangeFogAndDuplicatePortalsCountOnce()
    {
        FogZone[] zones = [new(10, 110, Vector3.One, true), new(10, 110, Vector3.One, true)];
        Vector4 plain = ZoneFogRules.Evaluate(zones, [], 0, Vector3.Zero, new(50, 0, 0));
        Assert.Equal(plain, ZoneFogRules.Evaluate(zones, [Portal(20), Portal(20)], 0, Vector3.Zero, new(50, 0, 0)));
    }

    [Fact]
    public void ReverseCrossingAndPolygonBoundsSelectCorrectZone()
    {
        FogZone[] zones = [new(0, 100, Vector3.UnitX, true), new(0, 100, Vector3.UnitY, true)];
        Vector4 reverse = ZoneFogRules.Evaluate(zones, [Portal(20)], 1, new(50, 0, 0), Vector3.Zero);
        Assert.Equal(.2f, reverse.X, 5);
        Assert.Equal(.3f, reverse.Y, 5);
        Vector4 outside = ZoneFogRules.Evaluate(zones, [Portal(20)], 0, new(0, 20, 0), new(50, 20, 0));
        Assert.Equal(new Vector4(.5f, 0, 0, .5f), outside);
    }

    [Theory]
    [InlineData(100f)]
    [InlineData(50f)]
    public void ZeroOrNegativeRampLengthUsesTheNativeSharpBoundary(float end)
    {
        FogZone[] zones = [new(100, end, Vector3.UnitX, true)];
        Assert.Equal(1e6f, FogZone.Rate(100, end));
        Assert.Equal(new Vector4(0, 0, 0, 1), ZoneFogRules.Evaluate(zones, [], 0, Vector3.Zero, new(99, 0, 0)));
        Assert.Equal(new Vector4(1, 0, 0, 0), ZoneFogRules.Evaluate(zones, [], 0, Vector3.Zero, new(101, 0, 0)));
    }
}
