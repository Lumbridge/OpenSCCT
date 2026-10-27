using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using Xunit;

namespace OpenScct.Core.Tests;

public sealed class StateTests
{
    [Fact]
    public void AutoStateIsEnteredAtSpawn()
    {
        var world = new World(1);
        SleeperActor s = world.Level.Spawn<SleeperActor>()!;
        Assert.Same(SleeperActor.Idle.Instance, s.StateNode);
        Assert.Equal(new Name("Idle"), s.GetStateName());
        Assert.True(s.IsInState("Idle"));
        Assert.Equal(["Idle.BeginState"], s.Log);
        Assert.Equal(SleeperActor.Idle.Instance.Id, s.StateFrame.StateId);
        Assert.Equal(1, s.StateFrame.Pc);
    }

    [Fact]
    public void InitialStateOverridesAuto()
    {
        var world = new World(1);
        SleeperActor s = world.Level.SpawnActor(new SleeperActor { InitialState = "Busy" }, default, default)!;
        Assert.Same(SleeperActor.Busy.Instance, s.StateNode);
        Assert.True(s.IsInState("Busy"));
        Assert.True(s.IsInState("Idle")); // Busy extends Idle
        Assert.False(s.IsInState("Waiting"));
    }

    [Fact]
    public void SleepWakesAfterTheUe2TickCount()
    {
        Assert.Equal(30, Actor.SleepTicks(0.5f, Sim.DeltaTime));
        Assert.Equal(1, Actor.SleepTicks(0f, Sim.DeltaTime));
        Assert.Equal(1, Actor.SleepTicks(-3f, Sim.DeltaTime));
        Assert.Equal(1, Actor.SleepTicks(0.008f, Sim.DeltaTime)); // under half a tick
        Assert.Equal(long.MaxValue, Actor.SleepTicks(float.NaN, Sim.DeltaTime));
        Assert.Equal(long.MaxValue, Actor.SleepTicks(1e9f, Sim.DeltaTime));

        var rng = new Random(8);
        for (int i = 0; i < 2000; i++)
        {
            float seconds = (float)(rng.NextDouble() * 20.0);
            Assert.Equal(ReferenceSleepTicks(seconds, Sim.DeltaTime), Actor.SleepTicks(seconds, Sim.DeltaTime));
        }
    }

    // UE2 execPollSleep, one poll per tick: LatentFloat -= DeltaSeconds; wake when LatentFloat < 0.5 * DeltaSeconds.
    private static long ReferenceSleepTicks(float seconds, float dt)
    {
        float latent = seconds;
        long polls = 0;
        while (true)
        {
            polls++;
            latent -= dt;
            if (latent < 0.5 * dt)
            {
                return polls;
            }
        }
    }

    [Fact]
    public void LatentSleepExample()
    {
        var world = new World(1);
        SleeperActor s = world.Level.Spawn<SleeperActor>()!;
        Assert.Equal(0, s.Counter);

        // Tick 1: Begin runs, Counter = 1, then Sleep(0.5) waits 30 ticks.
        world.Step();
        Assert.Equal(1, s.Counter);
        Assert.Equal(LatentWait.Sleep, s.StateFrame.Wait);
        Assert.Equal(31, s.StateFrame.UntilTick);

        world.Step(29);
        Assert.Equal(1, s.Counter);

        // Tick 31: wakes, Counter += 10, Goto Begin, Counter++, sleeps again.
        world.Step();
        Assert.Equal(12, s.Counter);
        Assert.Equal(61, s.StateFrame.UntilTick);

        world.Step(30);
        Assert.Equal(23, s.Counter);
    }

    [Fact]
    public void GotoStateSendsEndAndBeginState()
    {
        var world = new World(1);
        SleeperActor s = world.Level.Spawn<SleeperActor>()!;
        s.Log.Clear();
        Assert.Equal(EGotoState.GOTOSTATE_Success, s.GotoState("Busy"));
        Assert.Equal(["Idle.EndState", "Busy.BeginState"], s.Log);

        // Busy has no code of its own; its Begin label comes from Idle.
        Assert.Equal(1, s.StateFrame.Pc);
        world.Step();
        Assert.Equal(1, s.Counter);
    }

    [Fact]
    public void GotoSameStateOnlyRestartsCode()
    {
        var world = new World(1);
        SleeperActor s = world.Level.Spawn<SleeperActor>()!;
        world.Step();
        Assert.Equal(LatentWait.Sleep, s.StateFrame.Wait);
        s.Log.Clear();

        s.GotoState("Idle");
        Assert.Empty(s.Log);
        Assert.Equal(LatentWait.None, s.StateFrame.Wait);
        world.Step();
        Assert.Equal(2, s.Counter);
    }

    [Fact]
    public void GotoUnknownStateLeavesNoState()
    {
        var world = new World(1);
        SleeperActor s = world.Level.Spawn<SleeperActor>()!;
        s.Log.Clear();
        Assert.Equal(EGotoState.GOTOSTATE_NotFound, s.GotoState("NoSuchState"));
        Assert.Equal(["Idle.EndState"], s.Log);
        Assert.Same(ActorState.None, s.StateNode);
        Assert.Equal(new Name("SleeperActor"), s.GetStateName());
        Assert.False(s.IsInState("Idle"));
        Assert.Equal(LatentFrame.NoCode, s.StateFrame.Pc);
        Assert.Equal(0, s.StateFrame.StateId);
    }

    [Fact]
    public void GotoStateFromEndStateWins()
    {
        var world = new World(1);
        SleeperActor s = world.Level.Spawn<SleeperActor>()!;
        s.Log.Clear();
        s.RedirectOnIdleEnd = "Waiting";
        Assert.Equal(EGotoState.GOTOSTATE_Preempted, s.GotoState("Busy"));
        Assert.Same(SleeperActor.Waiting.Instance, s.StateNode);
        Assert.DoesNotContain("Busy.BeginState", s.Log);
    }

    [Fact]
    public void StateOverridesAndIgnores()
    {
        var world = new World(1);
        SleeperActor s = world.Level.Spawn<SleeperActor>()!;
        LoggingActor other = world.Level.Spawn<LoggingActor>()!;

        s.eventTouch(other);
        s.eventBump(other);
        Assert.Equal(1, s.GlobalTouches);
        Assert.Equal(1, s.GlobalBumps);

        s.GotoState("Busy");
        s.Log.Clear();
        s.eventTouch(other); // state version
        s.eventBump(other);  // ignored
        Assert.Equal(["Busy.Touch"], s.Log);
        Assert.Equal(1, s.GlobalTouches);
        Assert.Equal(1, s.GlobalBumps);

        s.GotoState(Name.None);
        s.eventTouch(other);
        Assert.Equal(2, s.GlobalTouches);
    }

    [Fact]
    public void WaitForLandingThenLoopThenChangeState()
    {
        var world = new World(1);
        SleeperActor s = world.Level.Spawn<SleeperActor>()!;
        s.Physics = EPhysics.PHYS_Falling;
        s.GotoState("Waiting");
        world.Step(5);
        Assert.Equal(LatentWait.WaitForLanding, s.StateFrame.Wait);
        Assert.DoesNotContain("Landed", s.Log);

        s.Physics = EPhysics.PHYS_None;
        world.Step();
        Assert.Contains("Landed", s.Log);
        Assert.Equal(LatentWait.Sleep, s.StateFrame.Wait);

        long sleep = Actor.SleepTicks(0.1f, Sim.DeltaTime);
        world.Step((int)sleep * 3);

        // After three loops the code goes to Idle, and Idle's code starts in the same tick.
        Assert.Equal(3, s.LoopCount);
        Assert.Same(SleeperActor.Idle.Instance, s.StateNode);
        Assert.Equal(1, s.Counter);
    }

    [Fact]
    public void SubclassRedefinesInheritedState()
    {
        var world = new World(1);
        SubSleeper s = world.Level.Spawn<SubSleeper>()!;
        Assert.Same(SubSleeper.Idle.Instance, s.StateNode);
        Assert.True(s.StateNode.IsAuto);
        Assert.True(s.IsInState("Idle"));

        // Code comes from the parent class's Idle.
        world.Step();
        Assert.Equal(1, s.Counter);

        // The redefinition adds a Timer override; BeginState/EndState are inherited.
        s.SetTimer(Sim.DeltaTime, true);
        world.Step(3);
        Assert.Equal(3, s.StateTimers);

        // Busy is inherited unchanged and extends the parent's Idle.
        s.GotoState("Busy");
        Assert.Same(SleeperActor.Busy.Instance, s.StateNode);
        world.Step(3);
        Assert.Equal(3, s.StateTimers);
    }

    [Fact]
    public void SubclassFunctionsDispatchThroughInterfaces()
    {
        var world = new World(1);
        GuardActor g = world.Level.Spawn<GuardActor>()!;
        Assert.Equal(3, g.eventPing(3));
        g.GotoState("Alert");
        Assert.Equal(30, g.eventPing(3));
        g.GotoState("Calm");
        Assert.Equal(3, g.eventPing(3));

        EliteGuard e = world.Level.Spawn<EliteGuard>()!;
        Assert.Equal(4, e.eventPing(3));
        e.GotoState("Alert");
        Assert.Same(EliteGuard.Alert.Instance, e.StateNode);
        Assert.Equal(35, e.eventPing(3));
        e.GotoState("Calm");
        Assert.Equal(4, e.eventPing(3));
    }

    [Fact]
    public void FrameRestoresIntoAnotherActor()
    {
        var world = new World(1);
        SleeperActor a = world.Level.Spawn<SleeperActor>()!;
        world.Step(10);
        LatentFrame saved = a.StateFrame;

        SleeperActor b = world.Level.Spawn<SleeperActor>()!;
        b.GotoState("Waiting");
        b.RestoreStateFrame(saved);
        b.Counter = a.Counter;
        Assert.Same(a.StateNode, b.StateNode);
        Assert.Equal(a.StateFrame, b.StateFrame);

        world.Step(60);
        Assert.Equal(a.Counter, b.Counter);
        Assert.Equal(a.StateFrame, b.StateFrame);

        Assert.Throws<InvalidOperationException>(() => b.RestoreStateFrame(saved with { StateId = 12345 }));
        Assert.Throws<InvalidOperationException>(() => b.RestoreStateFrame(saved with { LabelId = 777 }));
    }

    [Fact]
    public void StateAndLabelIdsAreStableHashes()
    {
        Assert.Equal(StableHash.OfNameCaseless("SleeperActor.Idle"), SleeperActor.Idle.Instance.Id);
        Assert.Equal(StableHash.OfNameCaseless("sleeperactor.idle"), SleeperActor.Idle.Instance.Id);
        Assert.NotEqual(SleeperActor.Idle.Instance.Id, SubSleeper.Idle.Instance.Id);
        Assert.Equal(StableHash.OfNameCaseless("Begin"), SleeperActor.Idle.Instance.Labels[0].Id);

        // FNV-1a reference value: pins the hash so saved frames stay readable.
        Assert.Equal(unchecked((int)0x2B24D044u), StableHash.OfNameCaseless("a"));
    }

    [Fact]
    public void RunawayCodeIsStopped()
    {
        var world = new World(1);
        RunawayActor r = world.Level.Spawn<RunawayActor>()!;
        world.Step();
        Assert.Equal(Actor.MaxLatentStepsPerTick, r.Steps);
        Assert.Equal(1, r.RunawayLatentLoops);
        world.Step();
        Assert.Equal(2, r.RunawayLatentLoops);
    }

    [Fact]
    public void DestroyStopsLatentCode()
    {
        var world = new World(1);
        SleeperActor s = world.Level.Spawn<SleeperActor>()!;
        s.Destroy();
        Assert.Equal(LatentFrame.NoCode, s.StateFrame.Pc);
        world.Step();
        Assert.Equal(0, s.Counter);
    }

    [Fact]
    public void StateTableRejectsDuplicates()
    {
        Assert.Throws<ArgumentException>(() => new StateTable(null, SleeperActor.Idle.Instance, SleeperActor.Idle.Instance));
        StateTable t = SubSleeper.ClassStates;
        Assert.Same(SubSleeper.Idle.Instance, t.Find("idle"));
        Assert.Same(SubSleeper.Idle.Instance, t.AutoState);
        Assert.Same(SleeperActor.Idle.Instance, t.FindById(SleeperActor.Idle.Instance.Id));
        Assert.Same(ActorState.None, t.FindById(0));
        Assert.Null(t.Find(Name.None));
        Assert.Equal(3, t.Count);
    }
}
