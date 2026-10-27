using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

namespace OpenScct.Core.Tests;

/// <summary>
/// Presentation events raised between two ticks (a network call received before the step, the player's input
/// commands, the views after it) can be read as <see cref="SimEvents.Late"/> until the next step; the ticks' own
/// events and the sink are unchanged.
/// </summary>
public sealed class SimEventsLateTests
{
    private sealed class Sink : ISimEventSink
    {
        public readonly List<string> Seen = new();

        public void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events)
        {
            foreach (SimEvent e in events)
            {
                Seen.Add(e.What.ToString());
            }
        }
    }

    private sealed class Ticker : Actor
    {
        protected internal override void Tick(float DeltaTime) =>
            XLevel.World.Events.PlaySound(this, "Tick" + XLevel.TickCount);
    }

    private static List<string> Names(ReadOnlySpan<SimEvent> events)
    {
        var names = new List<string>();
        foreach (SimEvent e in events)
        {
            names.Add(e.What.ToString());
        }

        return names;
    }

    [Fact]
    public void EventsRaisedBetweenTicksAreLate()
    {
        var world = new World(1);
        Ticker a = world.Level.Spawn<Ticker>()!;
        var sink = new Sink();
        world.EventSink = sink;

        world.Events.PlaySound(a, "BeforeFirst");
        Assert.Equal(["BeforeFirst"], Names(world.Events.Late));
        world.Step();
        Assert.Empty(Names(world.Events.Late));

        world.Events.PlaySound(a, "Between");
        Assert.Equal(["Between"], Names(world.Events.Late));
        Assert.Equal(["Tick1", "Between"], Names(world.Events.Current));
        world.Step();

        // The step drops them; the sink only ever sees the ticks' own events.
        Assert.Empty(Names(world.Events.Late));
        Assert.Equal(["Tick2"], Names(world.Events.Current));
        Assert.Equal(["Tick1", "Tick2"], sink.Seen);
    }

    [Fact]
    public void ReplayedEventsAreNeverLate()
    {
        var world = new World(1);
        Ticker a = world.Level.Spawn<Ticker>()!;
        world.Step();
        world.IsReplaying = true;
        world.Events.PlaySound(a, "Replayed");
        world.IsReplaying = false;
        Assert.Empty(Names(world.Events.Late));
    }
}
