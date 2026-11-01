using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class ModernSynchronizationTests
{
    [Fact]
    public void PredictionStorageRetains256FullTicksIncludingEveryBoundedMovementSplit()
    {
        var history = new OpenScct.Net.Moves.SavedMoveList(OpenScct.Net.Moves.SequencedMoveTransport.Capacity);
        for (int tick = 0; tick < 260; tick++)
            for (int split = 0; split < OpenScct.Net.Moves.SequencedMoveTransport.MaxCommandsPerTick; split++)
                history.Add(tick + split * 0.25f, default, EPhysics.PHYS_Walking, 0);
        Assert.Equal(4f, history.FirstSMove!.TimeStamp);
        Assert.Equal(259.75f, history.LastSMove!.TimeStamp);
        int count = 0;
        for (var move = history.FirstSMove; move is not null; move = move.NextMove) count++;
        Assert.Equal(OpenScct.Net.Moves.SequencedMoveTransport.HistoryTicks * 4, count);
    }

    [Fact]
    public void DecodingKnownAnimationNamesReusesTheirStorage()
    {
        var expected = new Name("NetParity.Anim_Étage");
        var writer = new FBitWriter(1024);
        writer.WriteString("netparity.anim_étage");
        var reader = new FBitReader(writer.Buffer, writer.NumBits);
        Assert.Equal(expected, reader.ReadName());
        // Warm the decoder and the runtime's span-based dictionary lookup before measuring steady state.
        for (int i = 0; i < 100; i++)
        {
            reader.SetData(writer.Buffer, 0, writer.NumBits);
            Assert.Equal(expected, reader.ReadName());
        }
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        bool same = true;
        for (int i = 0; i < 1000; i++)
        {
            reader.SetData(writer.Buffer, 0, writer.NumBits);
            same &= reader.ReadName() == expected;
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(same);
        Assert.Equal(0, allocated);
        reader.SetData(writer.Buffer, 0, writer.NumBits - 1);
        Assert.Equal(Name.None, reader.ReadName());
        Assert.True(reader.IsError);
    }

    [Fact]
    public void TransitBudgetDropsOverflowAndReusesStorageWithoutAllocating()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 9, SimLinkProfile.A, maxBufferedDatagrams: 2);
        using var a = network.Bind("a");
        using var b = network.Bind("b");
        EndpointId ab = a.Connect("b");
        b.Connect("a");
        byte[] buffer = new byte[1500];
        // Warm the send and receive paths before measuring the bounded storage reuse.
        a.Send(ab, buffer); Assert.True(b.TryReceive(buffer, out _, out _));
        long before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            a.Send(ab, buffer); a.Send(ab, buffer); a.Send(ab, buffer);
            b.TryReceive(buffer, out _, out _); b.TryReceive(buffer, out _, out _);
        }
        long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(100, network.QueueDrops);
        Assert.Equal(100, network.Lost);
        Assert.Equal(0, b.InFlightCount);
    }

    [Fact]
    public void IndependentHandwrittenExtensionsComposeWithoutReplacingEarlierMembers()
    {
        NetClassRegistry registry = NetRig.CreateRegistry();
        var first = new RepExtension();
        first.Functions.Add(new("Pose", true, false, "pose-1", static (_, _, _) => { }));
        registry.Extend<TestViewer>(first);
        var second = new RepExtension();
        second.Functions.Add(new("Equipment", true, false, "equipment-1", static (_, _, _) => { }));
        NetClassInfo info = registry.Extend<TestViewer>(second);
        Assert.NotNull(info.Layout.FindFunction("Pose"));
        Assert.NotNull(info.Layout.FindFunction("Equipment"));
        Assert.Throws<System.InvalidOperationException>(() => registry.Extend<TestViewer>(second));
    }

    [Fact]
    public void LateSnapshotCannotReplaceTheAuthoritativeReceiveReference()
    {
        var motion = new ProxyMotion();
        motion.Push(20, new ProxyState { Location = new FVector(20, 0, 0) });
        motion.Push(10, new ProxyState { Location = new FVector(-100, 0, 0) });
        var actor = new TestMover();
        motion.BeginReceive(actor);
        Assert.Equal(20f, actor.Location.X);
        Assert.Equal(20, motion.NewestTick);
        motion.Hold(21);
        Assert.True(motion.Sample(21 * NetTimeline.Fraction, 0, out ProxyState state));
        Assert.Equal(20f, state.Location.X);
    }

    [Fact]
    public void AHistoricalStateTransitionFillsMotionHistoryWithoutRewindingItsLatestReference()
    {
        var motion = new ProxyMotion();
        motion.Push(10, new ProxyState { Location = new FVector(10, 0, 0) });
        motion.Push(20, new ProxyState { Location = new FVector(20, 0, 0) });
        motion.Recover(15, new ProxyState { Location = new FVector(35, 0, 0) });
        Assert.Equal(20, motion.NewestTick); Assert.Equal(20f, motion.LatestLocation.X);
        Assert.True(motion.Sample(15 * NetTimeline.Fraction, 0, out ProxyState sampled));
        Assert.Equal(35f, sampled.Location.X);
        Assert.True(motion.TryGetInterval(12 * NetTimeline.Fraction, 15 * NetTimeline.Fraction, out long first, out long last));
        Assert.Equal(10, first); Assert.Equal(15, last);
        Assert.False(motion.TryGetInterval(9 * NetTimeline.Fraction, 15 * NetTimeline.Fraction, out _, out _));
        Assert.False(motion.TryGetInterval(15 * NetTimeline.Fraction, 21 * NetTimeline.Fraction, out _, out _));
        for (int tick = 21; tick < 100; tick++) motion.Recover(tick, new ProxyState { Location = new FVector(tick, 0, 0) });
        motion.Recover(1, default);
        Assert.Equal(ProxyMotion.Capacity, motion.SnapshotCount);
        Assert.Equal(68, motion.OldestTick);
        Assert.Equal(99, motion.NewestTick); Assert.Equal(99f, motion.LatestLocation.X);
    }

    [Fact]
    public void HeldProxyStopsReportingMovementAfterExtrapolationRunsOut()
    {
        var motion = new ProxyMotion();
        motion.Push(10, new ProxyState { Velocity = new FVector(60, 0, 0) });
        Assert.True(motion.Sample(100 * NetTimeline.Fraction, 6, out ProxyState state));
        Assert.Equal(6f, state.Location.X, 4);
        Assert.Equal(FVector.Zero, state.Velocity);
    }

    [Fact]
    public void AdaptiveClockRecoversImmediatelyAfterClientStopsTickingDuringLoad()
    {
        var timeline = new NetTimeline();
        var clock = new PresentationClock();
        for (int tick = 0; tick < 120; tick++)
        {
            timeline.AddSample(tick + 300, tick);
            timeline.Advance();
            clock.Observe(tick + 300, tick);
            clock.Advance(tick, timeline, 4);
        }

        long before = clock.TimeFixed;
        // The host continues for ten seconds while the client's render scene blocks its main thread.
        timeline.AddSample(1020, 120);
        timeline.Advance();
        clock.Observe(1020, 120);
        long resumed = clock.Advance(120, timeline, 4);
        Assert.True(resumed >= before);
        Assert.Equal(timeline.RenderTimeFixed(120, clock.DelayTicks), resumed);
    }

    [Fact]
    public void AdaptiveClockNeverRewindsWhenDelayOrTimelineChanges()
    {
        var timeline = new NetTimeline();
        var clock = new PresentationClock();
        long previous = long.MinValue;
        for (int tick = 0; tick < 500; tick++)
        {
            int lag = tick < 100 ? 3 : tick < 250 ? 7 + tick % 4 : 3;
            timeline.AddSample(tick - lag, tick);
            clock.Observe(tick - lag, tick);
            timeline.Advance();
            long now = clock.Advance(tick, timeline, 2);
            Assert.True(now >= previous);
            Assert.InRange(clock.DelayTicks, 2, 6);
            previous = now;
        }
    }

    [Fact]
    public void DirectedOutageAndAsymmetryLeaveTheOtherDirectionWorking()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 42, SimLinkProfile.A);
        using var a = network.Bind("a");
        using var b = network.Bind("b");
        EndpointId ab = a.Connect("b"), ba = b.Connect("a");
        network.SetLinkProfile("a", "b", SimLinkProfile.A with { OneWayLatencyMs = 100 });
        byte[] buffer = new byte[1500];
        a.Send(ab, new byte[] { 1 }); b.Send(ba, new byte[] { 2 });
        Assert.True(a.TryReceive(buffer, out _, out _));
        Assert.False(b.TryReceive(buffer, out _, out _));
        clock.Advance(100_000);
        Assert.True(b.TryReceive(buffer, out _, out _));
        network.PauseLink("a", "b", clock.NowMicroseconds + 250_000);
        a.Send(ab, new byte[] { 3 }); b.Send(ba, new byte[] { 4 });
        Assert.True(a.TryReceive(buffer, out _, out _));
        clock.Advance(350_000);
        Assert.False(b.TryReceive(buffer, out _, out _));
        a.Send(ab, new byte[] { 5 }); clock.Advance(100_000);
        Assert.True(b.TryReceive(buffer, out _, out _));
        Assert.Equal(5, buffer[0]);
    }
}
