using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

namespace OpenScct.Core.Tests;

public sealed class LevelTests
{
    private static LoggingActor SpawnLogged(Level level, EventLog log, string label, Name tag = default)
    {
        LoggingActor a = level.SpawnActor(new LoggingActor { Log = log, Label = label }, default, default)!;
        if (!tag.IsNone)
        {
            a.Tag = tag;
        }

        return a;
    }

    [Fact]
    public void LevelStartsWithLevelInfo()
    {
        var world = new World(1);
        Level level = world.Level;
        Assert.Single(level.Actors);
        Assert.Same(level.Info, level.Actors[0]);
        Assert.Equal(1, level.Info.ActorId);
        Assert.Same(level.Info, level.Info.Level);
        Assert.Equal(new Name("LevelInfo"), level.Info.Tag);
        Assert.Equal(ENetMode.NM_Standalone, level.Info.NetMode);
        Assert.Equal(ENetMode.NM_DedicatedServer, new World(1, ENetMode.NM_DedicatedServer).NetMode);
    }

    [Fact]
    public void SpawnRunsEventsInUe2Order()
    {
        var world = new World(1);
        var log = new EventLog();
        LoggingActor a = SpawnLogged(world.Level, log, "A");
        Assert.Equal(
            ["A.PreBeginPlay", "A.BeginPlay", "A.PostBeginPlay", "A.SetInitialState", "A.PostNetBeginPlay"],
            log.Entries);
        Assert.Equal(2, a.ActorId);
        Assert.Same(world.Level, a.XLevel);
        Assert.Same(world.Level.Info, a.Level);
        Assert.Equal(new Name("LoggingActor"), a.Tag);
        Assert.True(a.bScriptInitialized);
        Assert.Same(a, world.Level.FindActor(a.ActorId));
    }

    [Fact]
    public void RemoteOwnedSpawnSkipsPostNetBeginPlay()
    {
        var world = new World(1);
        var log = new EventLog();
        world.Level.SpawnActor(new LoggingActor { Log = log }, default, default, bRemoteOwned: true);
        Assert.DoesNotContain("PostNetBeginPlay", log.Entries);
    }

    [Fact]
    public void SpawnSetsLocationOwnerInstigatorAndTag()
    {
        var world = new World(1);
        var log = new EventLog();
        LoggingActor owner = SpawnLogged(world.Level, log, "Owner");
        TestPawn pawn = world.Level.Spawn<TestPawn>()!;
        log.Entries.Clear();

        var loc = new FVector(1f, 2f, 3f);
        var rot = new FRotator(10, 20, 30);
        LoggingActor child = world.Level.Spawn<LoggingActor>(loc, rot, owner, pawn, "Kid")!;
        Assert.Equal(loc, child.Location);
        Assert.Equal(rot, child.Rotation);
        Assert.Same(owner, child.Owner);
        Assert.Same(pawn, child.Instigator);
        Assert.Equal(new Name("Kid"), child.Tag);
        Assert.Equal(["Owner.GainedChild"], log.Entries);
        Assert.Same(child, world.Level.AllActors<LoggingActor>("Kid").ToList().Single());
    }

    [Fact]
    public void ActorSpawnHelperDefaultsToSpawnerPlacement()
    {
        var world = new World(1);
        LoggingActor spawner = world.Level.Spawn<LoggingActor>(new FVector(5f, 6f, 7f), new FRotator(1, 2, 3))!;
        LoggingActor made = spawner.Spawn<LoggingActor>(SpawnTag: "Made")!;
        Assert.Equal(spawner.Location, made.Location);
        Assert.Equal(spawner.Rotation, made.Rotation);
        Assert.Equal(new Name("Made"), made.Tag);
        Assert.Null(made.Owner);
    }

    [Fact]
    public void StaticAndNoDeleteOnlySpawnWhileLoading()
    {
        var world = new World(1);
        Assert.Null(world.Level.Spawn<StaticActor>());
        Assert.Null(world.Level.Spawn<NoDeleteActor>());

        var loading = new World(1);
        loading.Level.BeginLoad();
        Assert.NotNull(loading.Level.Spawn<StaticActor>());
        Assert.NotNull(loading.Level.Spawn<NoDeleteActor>());
        loading.Level.EndLoad();
        Assert.Equal(3, loading.Level.ActorCount);
    }

    [Fact]
    public void ActorsTickInSpawnOrder()
    {
        var world = new World(1);
        var log = new EventLog();
        SpawnLogged(world.Level, log, "A");
        SpawnLogged(world.Level, log, "B");
        SpawnLogged(world.Level, log, "C");
        log.Entries.Clear();
        world.Step();
        Assert.Equal(["A.Tick", "B.Tick", "C.Tick"], log.Entries);
    }

    [Fact]
    public void StaticActorsDoNotTick()
    {
        var world = new World(1);
        world.Level.BeginLoad();
        StaticActor s = world.Level.Spawn<StaticActor>()!;
        world.Level.EndLoad();
        world.Step(3);
        Assert.Equal(0, s.TickCalls);
    }

    [Fact]
    public void SpawnDuringTickJoinsAtEndOfTick()
    {
        var world = new World(1);
        var log = new EventLog();
        LoggingActor? spawned = null;
        LoggingActor a = SpawnLogged(world.Level, log, "A");
        SpawnLogged(world.Level, log, "B");
        a.OnTick = self =>
        {
            if (spawned is null)
            {
                spawned = self.XLevel.SpawnActor(new LoggingActor { Log = log, Label = "New" }, default, default);
                Assert.Equal(1, self.XLevel.PendingSpawnCount);
                Assert.DoesNotContain(spawned!, self.XLevel.Actors);
                Assert.Equal(2, self.XLevel.AllActors<LoggingActor>().Count());
            }
        };

        log.Entries.Clear();
        world.Step();

        // The new actor ran its spawn events at once but did not tick this tick.
        Assert.Equal(
            ["A.Tick", "New.PreBeginPlay", "New.BeginPlay", "New.PostBeginPlay", "New.SetInitialState", "New.PostNetBeginPlay", "B.Tick"],
            log.Entries);
        Assert.NotNull(spawned);
        Assert.Equal(0, world.Level.PendingSpawnCount);
        Assert.Same(spawned, world.Level.Actors[^1]);

        log.Entries.Clear();
        world.Step();
        Assert.Equal(["A.Tick", "B.Tick", "New.Tick"], log.Entries);
    }

    [Fact]
    public void DestroyDuringTickIsDeferred()
    {
        var world = new World(1);
        var log = new EventLog();
        LoggingActor a = SpawnLogged(world.Level, log, "A");
        LoggingActor b = SpawnLogged(world.Level, log, "B");
        LoggingActor c = SpawnLogged(world.Level, log, "C");
        a.OnTick = _ =>
        {
            if (!b.bDeleteMe)
            {
                Assert.True(b.Destroy());
                Assert.True(b.bDeleteMe);
                Assert.Contains(b, world.Level.Actors);
                Assert.DoesNotContain(b, world.Level.AllActors<LoggingActor>().ToList());
            }
        };

        log.Entries.Clear();
        world.Step();
        Assert.Equal(["A.Tick", "B.Destroyed", "C.Tick"], log.Entries);
        Assert.DoesNotContain(b, world.Level.Actors);
        Assert.Null(world.Level.FindActor(b.ActorId));
        Assert.Equal(new Actor[] { world.Level.Info, a, c }, world.Level.Actors.ToList());
        Assert.Equal(0, b.TickCalls);
    }

    [Fact]
    public void DestroyBetweenTicksLeavesListAtNextTick()
    {
        var world = new World(1);
        var log = new EventLog();
        LoggingActor a = SpawnLogged(world.Level, log, "A");
        LoggingActor b = SpawnLogged(world.Level, log, "B");
        b.Destroy();
        Assert.Contains(b, world.Level.Actors);
        Assert.Single(world.Level.AllActors<LoggingActor>().ToList());
        world.Step();
        Assert.DoesNotContain(b, world.Level.Actors);
        Assert.Equal(1, a.TickCalls);
        Assert.Equal(0, b.TickCalls);
    }

    [Fact]
    public void StaticAndNoDeleteCannotBeDestroyed()
    {
        var world = new World(1);
        world.Level.BeginLoad();
        StaticActor s = world.Level.Spawn<StaticActor>()!;
        NoDeleteActor n = world.Level.Spawn<NoDeleteActor>()!;
        world.Level.EndLoad();
        Assert.False(s.Destroy());
        Assert.False(n.Destroy());
        Assert.False(world.Level.Info.Destroy());
        Assert.False(s.bDeleteMe);
    }

    [Fact]
    public void DestroyDetachesAndClearsReferences()
    {
        var world = new World(1);
        var log = new EventLog();
        LoggingActor parent = SpawnLogged(world.Level, log, "Parent");
        LoggingActor rider = SpawnLogged(world.Level, log, "Rider");
        LoggingActor owned = SpawnLogged(world.Level, log, "Owned");
        LoggingActor toucher = SpawnLogged(world.Level, log, "Toucher");
        rider.SetBase(parent);
        owned.SetOwner(parent);
        parent.BeginTouch(toucher);
        Assert.Single(parent.Attached);
        Assert.Single(parent.Touching);
        log.Entries.Clear();

        parent.Destroy();
        Assert.Equal(["Parent.Destroyed", "Rider.BaseChange", "Toucher.UnTouch"], log.Entries);
        Assert.Null(rider.Base);
        Assert.Empty(parent.Attached);
        Assert.Empty(toucher.Touching);

        // Owner references are cleared once the destroy is applied (start of the next tick here).
        Assert.Same(parent, owned.Owner);
        world.Step();
        Assert.Null(owned.Owner);
    }

    [Fact]
    public void DestroyedDuringSpawnReturnsNull()
    {
        var world = new World(1);
        var a = new SelfDestroyer();
        Assert.Null(world.Level.SpawnActor(a, default, default));
        Assert.True(a.bDeleteMe);
        world.Step();
        Assert.DoesNotContain(a, world.Level.Actors);
    }

    private sealed class SelfDestroyer : Actor
    {
        protected internal override void BeginPlay() => Destroy();
    }

    [Fact]
    public void TagEventsFireInSpawnOrder()
    {
        var world = new World(1);
        var log = new EventLog();
        LoggingActor first = SpawnLogged(world.Level, log, "First", "Door");
        SpawnLogged(world.Level, log, "Other", "Light");
        LoggingActor second = SpawnLogged(world.Level, log, "Second", "Door");
        LoggingActor third = SpawnLogged(world.Level, log, "Third", "Door");
        TestPawn instigator = world.Level.Spawn<TestPawn>()!;
        log.Entries.Clear();

        world.Level.TriggerEvent("door", first, instigator);
        Assert.Equal(["First.Trigger", "Second.Trigger", "Third.Trigger"], log.Entries);
        Assert.Same(first, third.LastTriggerOther);
        Assert.Same(instigator, third.LastTriggerInstigator);

        log.Entries.Clear();
        third.UntriggerEvent("Door", null, null);
        Assert.Equal(["First.UnTrigger", "Second.UnTrigger", "Third.UnTrigger"], log.Entries);

        // Re-tagging keeps list order: First moves out, then back in front of Second.
        first.Tag = "Light";
        log.Entries.Clear();
        world.Level.TriggerEvent("Door", null, null);
        Assert.Equal(["Second.Trigger", "Third.Trigger"], log.Entries);
        first.Tag = "Door";
        log.Entries.Clear();
        world.Level.TriggerEvent("Door", null, null);
        Assert.Equal(["First.Trigger", "Second.Trigger", "Third.Trigger"], log.Entries);

        log.Entries.Clear();
        world.Level.TriggerEvent(Name.None, null, null);
        world.Level.TriggerEvent("NobodyHasThisTag", null, null);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void TriggerSkipsStaticAndDestroyedActors()
    {
        var world = new World(1);
        var log = new EventLog();
        world.Level.BeginLoad();
        StaticActor s = world.Level.Spawn<StaticActor>(tag: "Switch")!;
        s.Log = log;
        s.Label = "Static";
        world.Level.EndLoad();
        LoggingActor live = SpawnLogged(world.Level, log, "Live", "Switch");
        LoggingActor dead = SpawnLogged(world.Level, log, "Dead", "Switch");
        dead.Destroy();
        log.Entries.Clear();

        world.Level.TriggerEvent("Switch", null, null);
        Assert.Equal(["Live.Trigger"], log.Entries);
        Assert.Equal(2, world.Level.AllActors<Actor>("Switch").Count()); // static one is still listed
        Assert.Equal(1, world.Level.DynamicActors<Actor>("Switch").Count());
    }

    [Fact]
    public void TriggerHandlersCanChainEventsAndDestroy()
    {
        var world = new World(1);
        var log = new EventLog();
        var relay = world.Level.SpawnActor(new RelayActor { Log = log, Label = "Relay", Next = "Stage2" }, default, default)!;
        relay.Tag = "Stage1";
        LoggingActor victim = SpawnLogged(world.Level, log, "Victim", "Stage1");
        LoggingActor end = SpawnLogged(world.Level, log, "End", "Stage2");
        relay.Victim = victim;
        log.Entries.Clear();

        world.Level.TriggerEvent("Stage1", null, null);
        Assert.Equal(["Relay.Trigger", "Victim.Destroyed", "End.Trigger"], log.Entries);
    }

    private sealed class RelayActor : LoggingActor
    {
        public Name Next;
        public Actor? Victim;

        protected internal override void Trigger(Actor? Other, Pawn? EventInstigator)
        {
            base.Trigger(Other, EventInstigator);
            Victim?.Destroy();
            TriggerEvent(Next, this, EventInstigator);
        }
    }

    [Fact]
    public void TickAfterOwnerAndBase()
    {
        var world = new World(1);
        var log = new EventLog();
        LoggingActor a = SpawnLogged(world.Level, log, "A");
        LoggingActor b = SpawnLogged(world.Level, log, "B");
        LoggingActor c = SpawnLogged(world.Level, log, "C");
        LoggingActor d = SpawnLogged(world.Level, log, "D");
        a.SetBase(b, bNotifyActor: false); // A rides B
        b.SetOwner(c);                     // B is owned by C
        log.Entries.Clear();

        world.Step();
        List<string> ticks = log.Entries.Where(e => e.EndsWith(".Tick")).ToList();
        Assert.Equal(["C.Tick", "D.Tick", "B.Tick", "A.Tick"], ticks);
    }

    [Fact]
    public void DeferredActorsTickNewestFirstLikeUe2()
    {
        var world = new World(1);
        var log = new EventLog();
        LoggingActor x = SpawnLogged(world.Level, log, "X");
        LoggingActor y = SpawnLogged(world.Level, log, "Y");
        LoggingActor owner = SpawnLogged(world.Level, log, "Owner");
        x.SetOwner(owner);
        y.SetOwner(owner);
        log.Entries.Clear();
        world.Step();
        List<string> ticks = log.Entries.Where(e => e.EndsWith(".Tick")).ToList();
        Assert.Equal(["Owner.Tick", "Y.Tick", "X.Tick"], ticks);
    }

    [Fact]
    public void OwnerBaseLoopStillTicksEveryone()
    {
        var world = new World(1);
        var log = new EventLog();
        LoggingActor a = SpawnLogged(world.Level, log, "A");
        LoggingActor b = SpawnLogged(world.Level, log, "B");
        a.SetOwner(b);
        b.SetBase(a, bNotifyActor: false);
        world.Step();
        Assert.Equal(1, a.TickCalls);
        Assert.Equal(1, b.TickCalls);
    }

    [Fact]
    public void SetBaseRefusesLoops()
    {
        var world = new World(1);
        LoggingActor a = world.Level.Spawn<LoggingActor>()!;
        LoggingActor b = world.Level.Spawn<LoggingActor>()!;
        a.SetBase(b);
        b.SetBase(a);
        Assert.Same(b, a.Base);
        Assert.Null(b.Base);
        a.SetBase(a);
        Assert.Same(b, a.Base);
    }

    [Fact]
    public void ClockAccumulatesInFloat()
    {
        var world = new World(1);
        float expected = 0f;
        for (int i = 1; i <= 600; i++)
        {
            world.Step();
            expected += Sim.DeltaTime;
            Assert.Equal(i, world.TickCount);
            Assert.Equal(expected, world.TimeSeconds);
            Assert.Equal(expected, world.Level.Info.TimeSeconds);
        }

        Assert.Equal(0.016666668f, Sim.DeltaTime);
        Assert.Equal(60, Sim.Hz);
    }

    [Fact]
    public void TimeDilationScalesDeltaTime()
    {
        var world = new World(1);
        world.Level.Info.TimeDilation = 0.5f;
        world.Step();
        Assert.Equal(Sim.DeltaTime * 0.5f, world.Level.DeltaTime);
        Assert.Equal(Sim.DeltaTime * 0.5f, world.TimeSeconds);
    }

    [Fact]
    public void OneShotTimerFiresOnceOnTheCrossingTick()
    {
        var world = new World(1);
        LoggingActor a = world.Level.Spawn<LoggingActor>()!;
        a.SetTimer(0.5f, false);

        int expectedTick = ExpectedFireTicks(0.5f, Sim.DeltaTime, 1)[0];
        for (int t = 1; t <= 100; t++)
        {
            world.Step();
            Assert.Equal(t >= expectedTick ? 1 : 0, a.TimerCalls);
        }

        Assert.Equal(0f, a.TimerRate);
        Assert.InRange(expectedTick, 30, 31);
    }

    [Fact]
    public void LoopingTimerKeepsTheRemainder()
    {
        var world = new World(1);
        LoggingActor a = world.Level.Spawn<LoggingActor>()!;
        var fired = new List<int>();
        a.OnTick = self => { };
        a.SetTimer(0.1f, true);
        int last = 0;
        for (int t = 1; t <= 600; t++)
        {
            world.Step();
            if (a.TimerCalls != last)
            {
                fired.Add(t);
                last = a.TimerCalls;
            }
        }

        Assert.Equal(ExpectedFireTicks(0.1f, Sim.DeltaTime, fired.Count), fired);
        Assert.InRange(fired.Count, 99, 101);
    }

    [Fact]
    public void FastTimerCallsTimerOncePerTick()
    {
        var world = new World(1);
        LoggingActor a = world.Level.Spawn<LoggingActor>()!;
        a.SetTimer(0.005f, true); // three periods per tick
        world.Step(10);
        Assert.Equal(10, a.TimerCalls);
        Assert.True(a.TimerCounter < a.TimerRate);
    }

    [Fact]
    public void SetTimerZeroStops()
    {
        var world = new World(1);
        LoggingActor a = world.Level.Spawn<LoggingActor>()!;
        a.SetTimer(0.05f, true);
        world.Step(10);
        int calls = a.TimerCalls;
        Assert.True(calls > 0);
        a.SetTimer(0f, false);
        world.Step(10);
        Assert.Equal(calls, a.TimerCalls);
    }

    // Replays UE2 UpdateTimers in float to get the ticks a timer should fire on.
    private static List<int> ExpectedFireTicks(float rate, float dt, int count)
    {
        var result = new List<int>();
        float counter = 0f;
        for (int tick = 1; result.Count < count && tick < 100000; tick++)
        {
            counter += dt;
            if (counter >= rate)
            {
                int passed = (int)(counter / rate);
                counter -= rate * passed;
                result.Add(tick);
            }
        }

        return result;
    }

    [Fact]
    public void LifeSpanDestroys()
    {
        var world = new World(1);
        var log = new EventLog();
        LoggingActor a = SpawnLogged(world.Level, log, "A");
        a.LifeSpan = 0.1f;
        int ticks = 0;
        while (!a.bDeleteMe && ticks < 100)
        {
            world.Step();
            ticks++;
        }

        float life = 0.1f;
        int expected = 0;
        do
        {
            life -= Sim.DeltaTime;
            expected++;
        }
        while (!(life <= 0.0001f));

        Assert.Equal(expected, ticks);
        Assert.Single(log.Entries, "A.Destroyed");
        Assert.DoesNotContain(a, world.Level.Actors);
    }

    [Fact]
    public void LoadRunsBeginPlayAsPasses()
    {
        var world = new World(1);
        var log = new EventLog();
        Level level = world.Level;
        level.BeginLoad();
        LoggingActor a = SpawnLogged(level, log, "A");
        LoggingActor b = SpawnLogged(level, log, "B");
        Assert.Empty(log.Entries);
        Assert.Equal(3, level.ActorCount);
        Assert.False(level.Info.bBegunPlay);
        level.EndLoad();

        Assert.Equal(
            [
                "A.PreBeginPlay", "B.PreBeginPlay",
                "A.BeginPlay", "B.BeginPlay",
                "A.PostBeginPlay", "B.PostBeginPlay",
                "A.SetInitialState", "B.SetInitialState",
                "A.PostNetBeginPlay", "B.PostNetBeginPlay",
            ],
            log.Entries);
        Assert.True(level.Info.bBegunPlay);
        Assert.True(a.bScriptInitialized && b.bScriptInitialized);
    }

    [Fact]
    public void SpawnDuringLoadPassesGetsItsOwnEvents()
    {
        var world = new World(1);
        var log = new EventLog();
        Level level = world.Level;
        level.BeginLoad();
        var spawner = level.SpawnActor(new SpawnOnBeginPlay { Log = log, Label = "Spawner" }, default, default)!;
        level.EndLoad();

        int childEvents = log.Entries.Count(e => e.StartsWith("Child."));
        Assert.Equal(5, childEvents);
        Assert.Equal(1, log.Entries.Count(e => e == "Child.PreBeginPlay"));
        Assert.NotNull(spawner.Child);
    }

    private sealed class SpawnOnBeginPlay : LoggingActor
    {
        public LoggingActor? Child;

        protected internal override void BeginPlay()
        {
            base.BeginPlay();
            Child = XLevel.SpawnActor(new LoggingActor { Log = Log, Label = "Child" }, default, default);
        }
    }

    [Fact]
    public void ControllersFormTheControllerList()
    {
        var world = new World(1);
        TestController c1 = world.Level.Spawn<TestController>()!;
        TestController c2 = world.Level.Spawn<TestController>()!;
        PlayerController pc = world.Level.Spawn<PlayerController>()!;
        Assert.Same(pc, world.Level.Info.ControllerList);
        Assert.Same(c2, pc.nextController);
        Assert.Same(c1, c2.nextController);
        Assert.Null(c1.nextController);
        Assert.True(pc.bIsAPlayerController);

        c2.Destroy();
        Assert.Same(c1, pc.nextController);
        pc.Destroy();
        Assert.Same(c1, world.Level.Info.ControllerList);
    }

    [Fact]
    public void PawnAndControllerReferencesClearOnDestroy()
    {
        var world = new World(1);
        TestPawn pawn = world.Level.Spawn<TestPawn>()!;
        TestController controller = world.Level.Spawn<TestController>()!;
        controller.Pawn = pawn;
        pawn.Controller = controller;
        Assert.True(pawn.bIsAPawn);
        pawn.Destroy();
        world.Step();
        Assert.Null(controller.Pawn);
    }

    [Fact]
    public void SortActorListGroupsLikeUe2()
    {
        var world = new World(1);
        Level level = world.Level;
        level.BeginLoad();
        LoggingActor dyn1 = level.Spawn<LoggingActor>(tag: "T")!;
        StaticActor stat = level.Spawn<StaticActor>(tag: "T")!;
        LoggingActor local = level.Spawn<LoggingActor>(tag: "T")!;
        local.RemoteRole = ENetRole.ROLE_None;
        LoggingActor dyn2 = level.Spawn<LoggingActor>()!;
        level.EndLoad();
        level.SortActorList();

        Assert.Equal(new Actor[] { level.Info, stat, local, dyn1, dyn2 }, level.Actors.ToList());
        Assert.Equal(new Actor[] { stat, local, dyn1 }, level.AllActors<Actor>("T").ToList());

        LoggingActor late = level.Spawn<LoggingActor>(tag: "T")!;
        Assert.Same(late, level.Actors[^1]);
        Assert.Same(late, level.AllActors<Actor>("T").ToList()[^1]);
    }

    [Fact]
    public void PhysicsDriverRunsForMovingAuthorityActors()
    {
        var world = new World(1);
        var driver = new CountingPhysics();
        world.Level.PhysicsDriver = driver;
        LoggingActor still = world.Level.Spawn<LoggingActor>()!;
        LoggingActor walker = world.Level.Spawn<LoggingActor>()!;
        walker.Physics = EPhysics.PHYS_Walking;
        LoggingActor autonomous = world.Level.Spawn<LoggingActor>()!;
        autonomous.Physics = EPhysics.PHYS_Falling;
        autonomous.Role = ENetRole.ROLE_AutonomousProxy;
        world.Step(3);
        Assert.Equal(new Actor[] { walker, walker, walker }, driver.Calls);
        Assert.Equal(Sim.DeltaTime, driver.LastDelta);
        Assert.NotNull(still);
    }

    private sealed class CountingPhysics : IActorPhysics
    {
        public readonly List<Actor> Calls = new();
        public float LastDelta;

        public void performPhysics(Actor Actor, float DeltaSeconds)
        {
            Calls.Add(Actor);
            LastDelta = DeltaSeconds;
        }
    }

    [Fact]
    public void TouchBookkeeping()
    {
        var world = new World(1);
        var log = new EventLog();
        LoggingActor a = SpawnLogged(world.Level, log, "A");
        LoggingActor b = SpawnLogged(world.Level, log, "B");
        log.Entries.Clear();
        a.BeginTouch(b);
        a.BeginTouch(b);
        Assert.Equal(["A.Touch", "B.Touch"], log.Entries);
        Assert.Same(b, a.Touching.Single());
        Assert.Same(a, b.Touching.Single());
        log.Entries.Clear();
        a.EndTouch(b);
        Assert.Equal(["A.UnTouch", "B.UnTouch"], log.Entries);
        Assert.Empty(a.Touching);
        Assert.Empty(b.Touching);
    }

    [Fact]
    public void AllActorsFiltersByType()
    {
        var world = new World(1);
        world.Level.Spawn<LoggingActor>();
        world.Level.Spawn<TestPawn>();
        world.Level.Spawn<TestController>();
        world.Level.Spawn<PlayerController>();
        Assert.Equal(1, world.Level.AllActors<Pawn>().Count());
        Assert.Equal(2, world.Level.AllActors<Controller>().Count());
        Assert.Equal(1, world.Level.AllActors<PlayerController>().Count());

        // The player controller spawned its PlayerReplicationInfo (Controller.PostBeginPlay).
        Assert.Equal(1, world.Level.AllActors<PlayerReplicationInfo>().Count());
        Assert.Equal(6, world.Level.AllActors<Actor>().Count());
        Assert.Equal(5, world.Level.DynamicActors<Actor>().Count());
    }
}

internal static class ActorIteratorExtensions
{
    public static List<T> ToList<T>(this ActorIterator<T> iterator)
        where T : Actor
    {
        var list = new List<T>();
        foreach (T a in iterator)
        {
            list.Add(a);
        }

        return list;
    }
}
