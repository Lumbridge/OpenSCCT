using System;
using OpenScct.Core.Engine;

namespace OpenScct.Core;

/// <summary>
/// The simulation facade: one level, its presentation events and its seeded random streams. Hosts (server,
/// listen server, offline client, tests) create a World and call <see cref="Step()"/> once per fixed tick.
/// </summary>
public sealed class World
{
    public World(ulong matchSeed, ENetMode netMode = ENetMode.NM_Standalone)
    {
        MatchSeed = matchSeed;
        Events = new SimEvents(this);
        Random = new SimRandom(matchSeed, stream: 0, purpose: 0);
        Level = new Level(this);
        Level.Info.NetMode = netMode;
    }

    /// <summary>Seed every random stream of the match derives from.</summary>
    public ulong MatchSeed { get; }

    public Level Level { get; }

    /// <summary>Presentation events of the current or last tick.</summary>
    public SimEvents Events { get; }

    /// <summary>Called after every step with that tick's events; optional.</summary>
    public ISimEventSink? EventSink { get; set; }

    /// <summary>The gameplay random stream (stream 0, purpose 0) behind UnrealScript FRand, Rand and VRand.</summary>
    public SimRandom Random { get; }

    /// <summary>
    /// Set while resimulating ticks that were already presented (prediction replay, rollback). Presentation events
    /// are dropped while it is set.
    /// </summary>
    public bool IsReplaying { get; set; }

    public ENetMode NetMode => Level.Info.NetMode;

    public long TickCount => Level.TickCount;

    public float TimeSeconds => Level.TimeSeconds;

    /// <summary>
    /// Creates an independent random stream for (<see cref="MatchSeed"/>, <paramref name="stream"/>,
    /// <paramref name="purpose"/>). Stream 0 with purpose 0 is the one <see cref="Random"/> uses.
    /// </summary>
    public SimRandom CreateRandom(int stream, int purpose) => new(MatchSeed, stream, purpose);

    /// <summary>Runs one fixed tick.</summary>
    public void Step()
    {
        Level.Tick();
        EventSink?.OnSimEvents(Level.TickCount, Events.Current);
    }

    /// <summary>Runs <paramref name="ticks"/> fixed ticks.</summary>
    public void Step(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        for (int i = 0; i < ticks; i++)
        {
            Step();
        }
    }
}
