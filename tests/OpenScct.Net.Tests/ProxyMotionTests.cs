using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class ProxyMotionTests
{
    private const int F = NetTimeline.Fraction;

    private static void Push(ProxyMotion motion, Actor actor, long tick, FVector location, int yaw = 0, FVector velocity = default)
    {
        actor.Location = location;
        actor.Rotation = new FRotator(0, yaw, 0);
        actor.Velocity = velocity;
        motion.BeginReceive(actor);
        actor.Location = location;
        actor.Rotation = new FRotator(0, yaw, 0);
        actor.Velocity = velocity;
        motion.EndReceive(actor, tick);
    }

    [Fact]
    public void InterpolatesBetweenStates()
    {
        var motion = new ProxyMotion();
        var actor = new TestMover();
        Push(motion, actor, 10, new FVector(0f, 0f, 0f));
        Push(motion, actor, 13, new FVector(30f, 0f, 0f));
        Assert.True(motion.Sample(10 * F, 6, out FVector a, out _, out _));
        Assert.Equal(0f, a.X);
        Assert.True(motion.Sample(11 * F, 6, out FVector b, out _, out _));
        Assert.Equal(10f, b.X, 4);
        Assert.True(motion.Sample(12 * F + F / 2, 6, out FVector c, out _, out _));
        Assert.Equal(25f, c.X, 4);
    }

    [Fact]
    public void RotationTakesTheShortWayRound()
    {
        var motion = new ProxyMotion();
        var actor = new TestMover();
        Push(motion, actor, 0, FVector.Zero, yaw: 65000);
        Push(motion, actor, 2, FVector.Zero, yaw: 500);
        Assert.True(motion.Sample(F, 6, out _, out FRotator mid, out _));
        Assert.Equal((65000 + (500 + 65536 - 65000) / 2) & 65535, mid.Yaw);
    }

    [Fact]
    public void ExtrapolatesALittleThenHolds()
    {
        var motion = new ProxyMotion();
        var actor = new TestMover();
        Push(motion, actor, 10, FVector.Zero, velocity: new FVector(60f, 0f, 0f));
        Assert.True(motion.Sample(13 * F, 6, out FVector ahead, out _, out _));
        Assert.Equal(3f, ahead.X, 3);
        Assert.True(motion.Sample(100 * F, 6, out FVector held, out _, out _));
        Assert.Equal(6f, held.X, 3);
    }

    [Fact]
    public void HoldAddsAStateWithoutChange()
    {
        var motion = new ProxyMotion();
        var actor = new TestMover();
        Push(motion, actor, 10, new FVector(5f, 0f, 0f));
        motion.Hold(13);
        Push(motion, actor, 16, new FVector(35f, 0f, 0f));
        Assert.True(motion.Sample(13 * F, 0, out FVector atHold, out _, out _));
        Assert.Equal(5f, atHold.X);
        Assert.True(motion.Sample(15 * F, 0, out FVector moving, out _, out _));
        Assert.Equal(25f, moving.X, 4);
    }

    [Fact]
    public void OlderStatesAreIgnoredAndTheRingWraps()
    {
        var motion = new ProxyMotion();
        var actor = new TestMover();
        for (int i = 0; i < 100; i++)
        {
            Push(motion, actor, i * 3, new FVector(i, 0f, 0f));
        }

        Push(motion, actor, 3, new FVector(-1000f, 0f, 0f));
        Assert.Equal(ProxyMotion.Capacity, motion.SnapshotCount);
        Assert.Equal(99 * 3, motion.NewestTick);
        Assert.True(motion.Sample(98 * 3 * F, 0, out FVector v, out _, out _));
        Assert.Equal(98f, v.X);
    }

    [Fact]
    public void TimelineSnapsThenMovesSmoothly()
    {
        var timeline = new NetTimeline();
        timeline.AddSample(serverTick: 100, localTick: 40);
        Assert.Equal(60L * F, timeline.OffsetFixed);

        // A faster packet raises the estimate by 2 ticks; the offset follows at 1/16 tick per tick.
        timeline.AddSample(serverTick: 103, localTick: 41);
        long before = timeline.OffsetFixed;
        timeline.Advance();
        Assert.Equal(before + F / 16, timeline.OffsetFixed);
        for (int i = 0; i < 64; i++)
        {
            timeline.Advance();
        }

        Assert.Equal(62L * F, timeline.OffsetFixed);
        Assert.Equal((50L + 62 - 6) * F, timeline.RenderTimeFixed(50, 6));
    }

    [Fact]
    public void FineMovementRoundTrips()
    {
        RepLayout layout = NetRig.CreateRegistry().Find(typeof(TestMover))!.Layout;
        var source = new TestMover
        {
            Location = new FVector(1234.56f, -98.7f, 0.03f),
            Rotation = new FRotator(-100, 70000, 5),
            Velocity = new FVector(440f, -12.25f, 0f),
            Physics = EPhysics.PHYS_Falling,
        };

        var words = new int[layout.WordCount];
        var objects = new object?[layout.ObjectCount];
        layout.Capture(source, words, objects);
        string[] names = ["FineLocation", "FineRotation", "FineVelocity", "FinePhysics"];
        var w = new FBitWriter(1024);
        foreach (string name in names)
        {
            RepProperty p = layout.FindProperty(name)!;
            RepLeaf leaf = Assert.Single(p.Leaves);
            NetWire.WriteWords(w, leaf.Kind, leaf.EnumCount, words.AsSpan(p.WordOffset), objects.AsSpan(p.ObjectOffset), null!);
        }

        var target = new TestMover();
        var r = new FBitReader(w.Buffer, w.NumBits);
        foreach (string name in names)
        {
            RepLeaf leaf = layout.FindProperty(name)!.Leaves[0];
            NetValue v = default;
            NetWire.Read(r, leaf.Kind, leaf.EnumCount, ref v, null!);
            leaf.Apply(target, v);
        }

        Assert.False(r.IsError);
        Assert.Equal(NetQuantize.RoundTripVector(source.Location), target.Location);
        Assert.Equal(source.Rotation.Clamp(), target.Rotation);
        Assert.Equal(source.Velocity, target.Velocity);
        Assert.Equal(EPhysics.PHYS_Falling, target.Physics);
    }
}
