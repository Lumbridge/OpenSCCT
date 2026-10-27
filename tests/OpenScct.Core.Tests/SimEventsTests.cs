using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

namespace OpenScct.Core.Tests;

public sealed class SimEventsTests
{
    private sealed class NoisyActor : Actor
    {
        protected internal override void Tick(float DeltaTime)
        {
            XLevel.World.Events.PlaySound(this, "Step", slot: 2, volume: 0.5f, radius: 800f, pitch: 1.1f);
            if (XLevel.TickCount % 2 == 0)
            {
                XLevel.World.Events.SpawnEmitter(this, "Sparks", Location, Rotation);
                XLevel.World.Events.Notify(this, "Footstep", 3);
            }
        }
    }

    private sealed class Sink : ISimEventSink
    {
        public readonly List<(long Tick, int Count)> Calls = new();

        public void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events) => Calls.Add((tick, events.Length));
    }

    [Fact]
    public void EventsAreBufferedPerTick()
    {
        var world = new World(1);
        NoisyActor a = world.Level.Spawn<NoisyActor>(new FVector(1f, 2f, 3f))!;

        world.Step();
        ReadOnlySpan<SimEvent> tick1 = world.Events.Current;
        Assert.Equal(1, tick1.Length);
        Assert.Equal(SimEventKind.PlaySound, tick1[0].Kind);
        Assert.Equal(1, tick1[0].Tick);
        Assert.Equal(a.ActorId, tick1[0].ActorId);
        Assert.Equal(new Name("Step"), tick1[0].What);
        Assert.Equal(2, tick1[0].Slot);
        Assert.Equal(0.5f, tick1[0].Volume);
        Assert.Equal(800f, tick1[0].Radius);
        Assert.Equal(1.1f, tick1[0].Pitch);
        Assert.Equal(new FVector(1f, 2f, 3f), tick1[0].Location);

        world.Step();
        ReadOnlySpan<SimEvent> tick2 = world.Events.Current;
        Assert.Equal(3, tick2.Length);
        Assert.Equal(SimEventKind.SpawnEmitter, tick2[1].Kind);
        Assert.Equal(SimEventKind.Notify, tick2[2].Kind);
        Assert.Equal(3, tick2[2].Slot);
        Assert.Equal(2, tick2[2].Tick);
    }

    [Fact]
    public void ReplayingSuppressesEvents()
    {
        var world = new World(1);
        world.Level.Spawn<NoisyActor>();
        world.IsReplaying = true;
        Assert.True(world.Events.IsSuppressed);
        world.Step(4);
        Assert.Equal(0, world.Events.Count);
        world.IsReplaying = false;
        world.Step();
        Assert.Equal(1, world.Events.Count);
    }

    [Fact]
    public void SinkSeesEveryTick()
    {
        var world = new World(1);
        world.Level.Spawn<NoisyActor>();
        var sink = new Sink();
        world.EventSink = sink;
        world.Step(4);
        Assert.Equal([(1L, 1), (2L, 3), (3L, 1), (4L, 3)], sink.Calls);
    }

    [Fact]
    public void BufferGrows()
    {
        var world = new World(1);
        for (int i = 0; i < 600; i++)
        {
            world.Level.Spawn<NoisyActor>();
        }

        world.Step();
        Assert.Equal(600, world.Events.Count);
    }
}
