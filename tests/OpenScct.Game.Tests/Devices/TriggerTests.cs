using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>spec/devices/triggers.md: Engine.Trigger and SPresenceTrigger.</summary>
public sealed class TriggerTests
{
    private static (DeviceScene S, Trigger T, EventProbe Probe) Proximity(System.Action<Trigger>? setup = null)
    {
        DeviceScene s = DeviceScene.Create();
        var t = s.Add(new Trigger { Event = "sirene" });
        setup?.Invoke(t);
        EventProbe probe = s.Add(new EventProbe(), tag: "sirene");
        s.Begin();
        return (s, t, probe);
    }

    [Fact]
    public void APlayerEnteringFiresTheEventWithItselfAsInstigatorAndLeavingUntriggers()
    {
        (DeviceScene s, Trigger t, EventProbe probe) = Proximity();
        Assert.True(t.bOnlyAffectPawns);
        Assert.Equal(40f, t.CollisionRadius);
        Assert.True(t.IsInState("NormalTrigger"));
        TestSpy spy = s.Player<TestSpy>(default);
        t.BeginTouch(spy);
        t.EndTouch(spy);
        Assert.Equal(2, probe.Calls.Count);
        Assert.True(probe.Calls[0].Trigger);
        Assert.Same(t, probe.Calls[0].Other);
        Assert.Same(spy, probe.Calls[0].Instigator);
        Assert.False(probe.Calls[1].Trigger);
    }

    [Fact]
    public void RelevanceFollowsTheTriggerType()
    {
        (DeviceScene s, Trigger t, _) = Proximity();
        TestSpy player = s.Player<TestSpy>(default);
        TestMerc bot = s.Spawn(new TestMerc());
        Sender thing = s.Spawn(new Sender());

        Assert.True(t.IsRelevant(player));
        Assert.False(t.IsRelevant(bot));
        Assert.False(t.IsRelevant(thing));

        t.TriggerType = ETriggerType.TT_PawnProximity;
        Assert.True(t.IsRelevant(bot));
        Assert.False(t.IsRelevant(thing));

        t.TriggerType = ETriggerType.TT_HumanPlayerProximity;
        Assert.True(t.IsRelevant(player));
        Assert.False(t.IsRelevant(bot));

        t.TriggerType = ETriggerType.TT_ClassProximity;
        t.ClassProximityType = new ClassRef("SBase.SPAWNDEFENSE");
        Assert.True(t.IsRelevant(bot));
        Assert.False(t.IsRelevant(player));

        t.TriggerType = ETriggerType.TT_AnyProximity;
        Assert.True(t.IsRelevant(thing));

        t.bInitiallyActive = false;
        Assert.False(t.IsRelevant(thing));
    }

    [Fact]
    public void EveryEntryFiresAgainWithoutARetriggerDelay()
    {
        (DeviceScene s, Trigger t, EventProbe probe) = Proximity();
        TestSpy spy = s.Player<TestSpy>(default);
        for (int i = 0; i < 3; i++)
        {
            t.BeginTouch(spy);
            t.EndTouch(spy);
        }

        Assert.Equal(3, probe.Calls.FindAll(c => c.Trigger).Count);
    }

    [Fact]
    public void ARetriggerDelaySpacesTheFirings()
    {
        (DeviceScene s, Trigger t, EventProbe probe) = Proximity(t => t.ReTriggerDelay = 1f);
        TestSpy spy = s.Player<TestSpy>(default);
        s.StepSeconds(2f);
        t.BeginTouch(spy);
        t.EndTouch(spy);
        s.StepSeconds(0.5f);
        t.BeginTouch(spy);
        t.EndTouch(spy);
        s.StepSeconds(0.6f);
        t.BeginTouch(spy);
        Assert.Equal(2, probe.Calls.FindAll(c => c.Trigger).Count);
    }

    [Fact]
    public void AOnceOnlyTriggerTurnsItsCollisionOffAndResetRestoresIt()
    {
        (DeviceScene s, Trigger t, EventProbe probe) = Proximity(t => t.bTriggerOnceOnly = true);
        TestSpy spy = s.Player<TestSpy>(default);
        t.BeginTouch(spy);
        Assert.False(t.bCollideActors);
        t.Reset();
        Assert.True(t.bCollideActors);
        Assert.True(t.bInitiallyActive);
        Assert.Single(probe.Calls);
    }

    [Fact]
    public void ARepeatTimeFiresAgainWhileSomethingRelevantStaysInside()
    {
        (DeviceScene s, Trigger t, EventProbe probe) = Proximity(t => t.RepeatTriggerTime = 1f);
        TestSpy spy = s.Player<TestSpy>(default);
        t.BeginTouch(spy);
        s.StepSeconds(3.1f);
        Assert.Equal(4, probe.Calls.FindAll(c => c.Trigger).Count);
        t.EndTouch(spy);
        s.StepSeconds(3f);
        Assert.Equal(4, probe.Calls.FindAll(c => c.Trigger).Count);
    }

    [Fact]
    public void AShootTriggerAnswersInstigatedDamageOverItsThreshold()
    {
        (DeviceScene s, Trigger t, EventProbe probe) = Proximity(t =>
        {
            t.TriggerType = ETriggerType.TT_Shoot;
            t.DamageThreshold = 10f;
        });
        Assert.False(t.bHidden);
        Assert.True(t.bProjTarget);
        Assert.Equal(EDrawType.DT_None, t.DrawType);
        TestMerc shooter = s.Player<TestMerc>(default);
        t.eventTakeDamage(9, shooter, default, default, SDamageType.SDTBase);
        t.eventTakeDamage(50, null, default, default, SDamageType.SDTBase);
        t.eventTakeDamage(10, shooter, default, default, SDamageType.SDTBase);
        (_, _, _, Actor? other, Pawn? by) = Assert.Single(probe.Calls);
        Assert.Same(t, other);
        Assert.Same(shooter, by);
    }

    [Fact]
    public void OtherTriggerStatesSwitchTheTriggerAndTouchWhatIsInside()
    {
        (DeviceScene s, Trigger t, EventProbe probe) = Proximity(t =>
        {
            t.InitialState = "OtherTriggerToggles";
            t.bInitiallyActive = false;
        });
        TestSpy spy = s.Player<TestSpy>(default);
        t.BeginTouch(spy);
        Assert.Empty(probe.Calls);
        t.eventTrigger(null, null);
        Assert.True(t.bInitiallyActive);
        Assert.Single(probe.Calls);
        t.eventTrigger(null, null);
        Assert.False(t.bInitiallyActive);

        t.GotoState("OtherTriggerTurnsOn");
        t.eventTrigger(null, null);
        t.eventTrigger(null, null);
        Assert.True(t.bInitiallyActive);
        Assert.Equal(2, probe.Calls.Count);
        t.GotoState("OtherTriggerTurnsOff");
        t.eventTrigger(null, null);
        Assert.False(t.bInitiallyActive);
    }

    [Fact]
    public void AnInactiveTriggerFindsTheActorsThatTriggerIt()
    {
        DeviceScene s = DeviceScene.Create();
        Sender first = s.Add(new Sender { Event = "gate" });
        Sender second = s.Add(new Sender { Event = "gate" });
        Trigger t = s.Add(new Trigger { bInitiallyActive = false }, tag: "gate");
        s.Begin();
        Assert.Same(first, t.TriggerActor);
        Assert.Same(second, t.TriggerActor2);
    }

    // ---- SPresenceTrigger ---------------------------------------------------------------------------------

    private sealed class GrabController : Controller
    {
        public static new readonly StateTable ClassStates = new(Actor.ClassStates, Grabbing.Instance);

        public override StateTable GetStateTable() => ClassStates;

        public sealed class Grabbing : ActorState
        {
            public static readonly Grabbing Instance = new();

            private Grabbing()
                : base("SPlayerController", "s_GrabbingPlayer")
            {
            }
        }
    }

    private static (DeviceScene S, SPresenceTrigger T, EventProbe Probe) Presence(System.Action<SPresenceTrigger>? setup = null)
    {
        DeviceScene s = DeviceScene.Create();
        var t = s.Add(new SPresenceTrigger { Event = "PorteMerc" });
        setup?.Invoke(t);
        EventProbe probe = s.Add(new EventProbe(), tag: "PorteMerc");
        s.Begin();
        return (s, t, probe);
    }

    [Fact]
    public void APresenceTriggerFiresForItsDetectedClassOnly()
    {
        (DeviceScene s, SPresenceTrigger t, EventProbe probe) = Presence();
        Assert.Equal(new ClassRef("SBase.SPAWNDEFENSE"), t.DetectedClass);
        TestSpy spy = s.Player<TestSpy>(default);
        TestMerc merc = s.Player<TestMerc>(default);
        t.BeginTouch(spy);
        t.BeginTouch(merc);
        (_, _, bool trigger, Actor? other, Pawn? by) = Assert.Single(probe.Calls);
        Assert.True(trigger);
        Assert.Same(t, other);
        Assert.Same(merc, by);
    }

    [Fact]
    public void APresenceTriggerForAllPawnsAndAnUntriggeringOne()
    {
        (DeviceScene s, SPresenceTrigger t, EventProbe probe) = Presence(t =>
        {
            t.DetectedClass = new ClassRef("SBase.SBasePawn");
            t.bUntrigger = true;
        });
        TestSpy spy = s.Player<TestSpy>(default);
        t.BeginTouch(spy);
        Assert.False(Assert.Single(probe.Calls).Trigger);
    }

    [Fact]
    public void ASpyCarryingAGrabbedMercOpensAMercDoor()
    {
        (DeviceScene s, SPresenceTrigger t, EventProbe probe) = Presence(t => t.bUntrigger = true);
        TestSpy spy = s.Spawn(new TestSpy());
        TestMerc merc = s.Spawn(new TestMerc());
        GrabController grabber = s.Spawn(new GrabController());
        grabber.Pawn = spy;
        spy.Controller = grabber;
        spy.m_AttackTarget = merc;

        grabber.GotoState(Name.None);
        t.BeginTouch(spy);
        Assert.Empty(probe.Calls);
        t.EndTouch(spy);

        grabber.GotoState("s_GrabbingPlayer");
        t.BeginTouch(spy);
        (_, _, bool trigger, _, Pawn? by) = Assert.Single(probe.Calls);
        Assert.True(trigger);
        Assert.Same(spy, by);
    }
}
