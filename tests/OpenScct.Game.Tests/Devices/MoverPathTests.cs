using System;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

public sealed class MoverPathTests
{
    [Fact]
    public void LegacyMotionLeavesNativeReplicationFieldsUntouched()
    {
        var scene = DeviceScene.Create();
        var mover = scene.Add(new Mover { InitialState = "None" });
        mover.KeyPos[1] = new FVector(0, 0, 128); scene.Begin();
        mover.DoOpen(); scene.Step(70); mover.DoClose(); scene.Step(70);
        Assert.Equal(0, mover.CurrentKey); Assert.Equal(0, mover.PathTarget); Assert.Equal(0, mover.PathPrevious);
        Assert.Equal(FVector.Zero, mover.Location);
    }

    private static MoverPath Path(int count, int start = 0) => new(Enumerable.Range(0, count)
        .Select(i => new MoverPathKey(new FVector(i * 10, 0, i), new FRotator(0, i * 64, 0))).ToArray(), start);

    [Theory]
    [InlineData(16)]
    [InlineData(300)]
    public void OpensAndClosesAcrossLegacyArrayAndByteBoundaries(int count)
    {
        var scene = DeviceScene.Create();
        var mover = scene.Add(new Mover { Path = Path(count), InitialState = "None", MoveTime = .01f });
        scene.Begin(); mover.DoOpen(); scene.Step(count + 10);
        Assert.False(mover.bInterpolating); Assert.Equal(count - 1, mover.CurrentKey);
        Assert.Equal(mover.KeyPosition(count - 1), mover.Location);
        mover.DoClose(); scene.Step(count + 10);
        Assert.False(mover.bInterpolating); Assert.Equal(0, mover.CurrentKey); Assert.Equal(FVector.Zero, mover.Location);
    }

    [Fact]
    public void StartsAtNativeStopAndReversesTheCurrentLegWithoutJumping()
    {
        var scene = DeviceScene.Create();
        var mover = scene.Add(new Mover { Path = Path(300, 280), InitialState = "None", MoveTime = 1 });
        scene.Begin(); Assert.Equal(280, mover.CurrentKey); Assert.Equal(mover.KeyPosition(280), mover.Location);
        mover.InterpolateTo(281, 1); scene.Step(20); FVector before = mover.Location;
        mover.InterpolateTo(280, 1); scene.Step(1);
        Assert.InRange((mover.Location - before).Size(), 0, 1);
        scene.Step(60); Assert.True(mover.CurrentKey < 280); // closing continues through the preceding stops
    }

    [Fact]
    public void ClientUsesIntegerIndicesAndNativeLegTiming()
    {
        var path = Path(300);
        var scene = DeviceScene.Create();
        var server = scene.Add(new Mover { Path = path, CurrentKey = 280, PreviousKey = 280, InitialState = "None" });
        var client = scene.Add(new Mover { Path = path, InitialState = "None" });
        scene.Begin();
        var times = new MoverKeyTiming[300]; times[281] = new MoverKeyTiming(2, false);
        server.Legs = new MoverLegs(times); server.InterpolateTo(281, 1);
        client.Legs = server.Legs; client.SimOldPos = server.SimOldPos; client.SimInterpolate = server.SimInterpolate;
        client.PathTarget = server.PathTarget; client.PathPrevious = server.PathPrevious; client.Rotation = server.OldRot;
        client.ClientStartInterpolation();
        Assert.Equal(281, client.CurrentKey); Assert.Equal(280, client.PreviousKey); Assert.Equal(.5f, client.PhysRate);
        client.physMovingBrush(.25f); server.physMovingBrush(.25f);
        Assert.Equal(server.Location, client.Location);
    }

    [Fact]
    public void PathOwnsItsInputAndRejectsInvalidPoses()
    {
        var input = new[] { new MoverPathKey() }; var path = new MoverPath(input);
        input[0] = new(new FVector(1, 2, 3), default); Assert.Equal(FVector.Zero, path.Keys[0].Offset);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MoverPath([], 0));
        Assert.Throws<ArgumentException>(() => new MoverPath([new(new FVector(float.NaN, 0, 0), default)]));
    }
}
