using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

namespace OpenScct.Core.Tests;

public sealed class ActorIndexTests(ITestOutputHelper output)
{
    private static void SameAsFullScan<T>(Level level, bool dynamicOnly = false, Name tag = default) where T : Actor
    {
        var expected = new List<T>();
        foreach (Actor actor in level.Actors)
        {
            if (actor is T match && !actor.bDeleteMe && (!dynamicOnly || !actor.bStatic) && (tag.IsNone || actor.Tag == tag))
                expected.Add(match);
        }

        var actual = new List<T>();
        foreach (T actor in dynamicOnly ? level.DynamicActors<T>(tag) : level.AllActors<T>(tag)) actual.Add(actor);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TypeQueriesMatchFullScanThroughLoadFlagsDestroyAndSort()
    {
        var world = new World(9);
        Level level = world.Level;
        // Indexes requested before the first matching actor must still follow later spawns and subclasses.
        Assert.Equal(0, level.AllActors<Pawn>().Count());
        Assert.Equal(0, level.AllActors<LoggingActor>().Count());
        level.BeginLoad();
        for (int i = 0; i < 400; i++)
        {
            Actor actor = i % 7 == 0 ? new TestPawn() : i % 3 == 0 ? new StaticActor() : new LoggingActor();
            actor.RemoteRole = i % 2 == 0 ? ENetRole.ROLE_None : ENetRole.ROLE_SimulatedProxy;
            actor = level.SpawnActor(actor, default, default)!;
            actor.Tag = i % 4 == 0 ? new Name("shared") : new Name("other");
            SameAsFullScan<Pawn>(level);
            SameAsFullScan<LoggingActor>(level);
        }

        level.EndLoad();
        var random = new Random(912);
        for (int i = 0; i < 100; i++)
        {
            Actor actor = level.Actors[random.Next(1, level.ActorCount)];
            actor.bStatic = !actor.bStatic;
            actor.Tag = i % 2 == 0 ? new Name("shared") : new Name("other");
            if (i % 5 == 0 && !actor.bStatic) level.Destroy(actor);
            if (i % 7 == 0) level.Spawn<TestPawn>();
            if (i % 11 == 0) level.SortActorList();
            SameAsFullScan<Actor>(level);
            SameAsFullScan<Pawn>(level);
            SameAsFullScan<Pawn>(level, true);
            SameAsFullScan<LoggingActor>(level, true);
            SameAsFullScan<LoggingActor>(level, tag: "shared");
            SameAsFullScan<LoggingActor>(level, true, "shared");
        }

        world.Step();
        SameAsFullScan<Pawn>(level);
        SameAsFullScan<LoggingActor>(level);
    }

    [Fact]
    public void IteratorsSeeImmediateSpawnsAndSkipDestroyedActors()
    {
        var world = new World(4);
        Level level = world.Level;
        LoggingActor first = level.Spawn<LoggingActor>()!;
        LoggingActor deleted = level.Spawn<LoggingActor>()!;
        ActorIterator<LoggingActor> iterator = level.AllActors<LoggingActor>();
        Assert.True(iterator.MoveNext());
        Assert.Same(first, iterator.Current);
        level.Destroy(deleted);
        LoggingActor last = level.Spawn<LoggingActor>()!;
        Assert.True(iterator.MoveNext());
        Assert.Same(last, iterator.Current);
        Assert.False(iterator.MoveNext());
    }

    [Fact]
    public void TickSpawnsRemainPendingAndDestroyedPendingActorsNeverJoin()
    {
        var world = new World(3);
        Level level = world.Level;
        LoggingActor actor = level.Spawn<LoggingActor>()!;
        Assert.Equal(0, level.AllActors<Pawn>().Count());
        actor.OnTick = self =>
        {
            level.Spawn<TestPawn>();
            TestPawn discarded = level.Spawn<TestPawn>()!;
            level.Destroy(discarded);
            Assert.Equal(0, level.AllActors<Pawn>().Count());
            SameAsFullScan<Pawn>(level);
            self.OnTick = null;
        };
        world.Step();
        Assert.Equal(1, level.AllActors<Pawn>().Count());
        SameAsFullScan<Pawn>(level);
        world.IsReplaying = true;
        using (level.ReplayTime(0, 0f, Sim.DeltaTime)) SameAsFullScan<Pawn>(level);
    }

    [Fact]
    public void WarmQueriesAllocateNothingAndReportIndexedVersusFullScanCost()
    {
        var world = new World(6);
        Level level = world.Level;
        level.BeginLoad();
        for (int i = 0; i < 10000; i++) level.SpawnActor(new StaticActor(), default, default);
        for (int i = 0; i < 16; i++) level.Spawn<TestPawn>();
        level.EndLoad();
        List<Actor> all = (List<Actor>)level.Actors;
        long Scan(bool indexed, int iterations)
        {
            long checksum = 0;
            for (int i = 0; i < iterations; i++)
                foreach (Pawn pawn in indexed ? level.DynamicActors<Pawn>() : new ActorIterator<Pawn>(all, Name.None, true))
                    checksum += pawn.ActorId;
            return checksum;
        }

        Scan(false, 100);
        Scan(true, 100);
        const int repeats = 2000;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        long indexed = Scan(true, repeats);
        double indexedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        start = Stopwatch.GetTimestamp();
        long full = Scan(false, repeats);
        double fullMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Assert.Equal(full, indexed);
        Assert.Equal(0, bytes);
        output.WriteLine($"{repeats} pawn queries / {level.ActorCount} actors: full scan {fullMs:F3} ms, indexed {indexedMs:F3} ms; indexed allocation {bytes} B. Synthetic query cost, not game FPS.");
    }
}
