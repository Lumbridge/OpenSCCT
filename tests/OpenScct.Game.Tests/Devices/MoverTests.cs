using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>spec/devices/mover.md: interpolation, initialisation, triggering and the state machines (no collision).</summary>
public sealed class MoverTests
{
    private const float Tick = 1f / 60f;

    private sealed class ForcingSender : Actor
    {
        public override bool ForceTriggerableEvenIfMoving() => true;
    }

    private sealed class Door
    {
        public DeviceScene S = null!;
        public Mover M = null!;
        public Sender Switch = null!;
        public EventProbe Event = null!;
        public EventProbe Opening = null!;
        public EventProbe Opened = null!;
        public EventProbe Closing = null!;
        public EventProbe Closed = null!;
    }

    /// <summary>A two-key door rising 100 uu, with probes on its events. The switch comes before it in the list.</summary>
    private static Door MakeDoor(string state = "TriggerOpenTimed", float moveTime = 1f, float stayOpen = 4f,
        System.Action<Mover>? setup = null, ENetMode mode = ENetMode.NM_Standalone)
    {
        var d = new Door { S = DeviceScene.Create(mode) };
        d.Switch = d.S.Add(new Sender());
        d.M = d.S.Add(new Mover
        {
            InitialState = state,
            MoveTime = moveTime,
            StayOpenTime = stayOpen,
            Event = "ev",
            OpeningEvent = "opening",
            OpenedEvent = "opened",
            ClosingEvent = "closing",
            ClosedEvent = "closed",
            OpeningSound = new AssetRef("open"),
            OpenedSound = new AssetRef("opened"),
            ClosingSound = new AssetRef("close"),
            ClosedSound = new AssetRef("closed"),
            MoveAmbientSound = new AssetRef("loop"),
        }, default, tag: "door");
        d.M.KeyPos[1] = V.At(0f, 0f, 100f);
        setup?.Invoke(d.M);
        d.Event = d.S.Add(new EventProbe(), tag: "ev");
        d.Opening = d.S.Add(new EventProbe(), tag: "opening");
        d.Opened = d.S.Add(new EventProbe(), tag: "opened");
        d.Closing = d.S.Add(new EventProbe(), tag: "closing");
        d.Closed = d.S.Add(new EventProbe(), tag: "closed");
        d.S.Begin();
        return d;
    }

    /// <summary>The switch triggers the door inside the next tick; returns that tick's time.</summary>
    private static float Fire(Door d, string eventName = "door", Pawn? instigator = null)
    {
        d.Switch.FireNextTick(eventName, instigator);
        d.S.Step();
        return d.Switch.FiredAt;
    }

    [Fact]
    public void BeginPlayPlacesTheMoverOnItsKeyAndKeepsTheOriginalTimes()
    {
        DeviceScene s = DeviceScene.Create();
        var m = s.Add(new Mover { BasePos = V.At(10f, 0f, 0f), KeyNum = 1, DelayTime = 0.5f, MoveTime = 2f, StayOpenTime = 3f }, V.At(999f, 0f, 0f));
        m.KeyPos[1] = V.At(0f, 0f, 50f);
        m.KeyRot[1] = new FRotator(0, 4096, 0);
        s.Begin();
        Assert.Equal(V.At(10f, 0f, 50f), m.Location);
        Assert.Equal(new FRotator(0, 4096, 0), m.Rotation);
        Assert.Equal(1, m.PrevKeyNum);
        Assert.Equal(0.5f, m.OriginalDelayTime);
        Assert.Equal(2f, m.OriginalMoveTime);
        Assert.Equal(3f, m.OriginalStayOpenTime);
        Assert.Equal(EMoverEncroachType.ME_ReturnWhenEncroach, m.OriginalMoverEncroachType);
        Assert.Same(m, m.Leader);
        Assert.True(m.IsInState("BumpOpenTimed"));
    }

    [Fact]
    public void LegsFollowTheGlideCurveOrALineAndEndOnTheKey()
    {
        foreach (EMoverGlideType glide in new[] { EMoverGlideType.MV_GlideByTime, EMoverGlideType.MV_MoveByTime })
        {
            Door d = MakeDoor("None", setup: m => m.MoverGlideType = glide);
            d.M.InterpolateTo(1, 1f);
            d.S.Step(15);
            float a = 0.25f;
            float expected = glide == EMoverGlideType.MV_GlideByTime ? 100f * (3f * a * a - 2f * a * a * a) : 25f;
            Assert.Equal(expected, d.M.Location.Z, 2);
            d.S.Step(15);
            Assert.Equal(50f, d.M.Location.Z, 2);
            d.S.Step(31);
            Assert.False(d.M.bInterpolating);
            Assert.Equal(100f, d.M.Location.Z, 3);
            Assert.Equal(1, d.M.KeyNum);
            Assert.Equal(1, d.M.PrevKeyNum);
            Assert.Equal(0, d.M.ClientUpdate);
        }
    }

    [Fact]
    public void InterpolateToRecordsTheLegForClients()
    {
        Door d = MakeDoor("None");
        d.M.InterpolateTo(1, 2f);
        Assert.Equal(V.At(0f, 50f, 1f), d.M.SimInterpolate);
        Assert.Equal(d.M.Location, d.M.SimOldPos);
        Assert.Equal(0.5f, d.M.PhysRate);
        Assert.Equal(1, d.M.ClientUpdate);
        d.M.InterpolateTo(1, 0f);
        Assert.Equal(1f / 0.005f, d.M.PhysRate);
    }

    [Fact]
    public void ReversingALegContinuesFromWhereItIsAndTakesTheTimeSpent()
    {
        Door d = MakeDoor("None");
        d.M.InterpolateTo(1, 1f);
        d.S.Step(18);
        float z = d.M.Location.Z;
        float alpha = d.M.PhysAlpha;
        d.M.InterpolateTo(0, 1f);
        Assert.Equal(1f - alpha, d.M.PhysAlpha);
        d.S.Step(1);
        Assert.True(d.M.Location.Z < z && z - d.M.Location.Z < 10f, $"from {z} to {d.M.Location.Z}");
        d.S.Step(16);
        Assert.True(d.M.bInterpolating);
        d.S.Step(2);
        Assert.False(d.M.bInterpolating);
        Assert.Equal(0f, d.M.Location.Z, 3);
    }

    [Fact]
    public void KeyFrameReachedKeepsGoingThroughTheKeys()
    {
        Door d = MakeDoor("None", setup: m =>
        {
            m.NumKeys = 3;
            m.KeyPos[2] = V.At(0f, 0f, 300f);
        });
        d.M.DoOpen();
        d.S.Step(61);
        Assert.True(d.M.bInterpolating);
        Assert.Equal(2, d.M.KeyNum);
        d.S.Step(61);
        Assert.False(d.M.bInterpolating);
        Assert.Equal(300f, d.M.Location.Z, 3);
        Assert.True(d.M.AmbientSound.IsNone);
    }

    [Fact]
    public void TriggerOpenTimedOpensHoldsAndClosesWithItsEventsAndSounds()
    {
        Door d = MakeDoor();
        float t0 = Fire(d);
        Assert.Single(d.Opening.Calls);
        Assert.Equal(new AssetRef("loop"), d.M.AmbientSound);
        d.S.StepSeconds(7f);

        // Open over 1 s, Event and OpenedEvent at full open, hold 4 s, Event untriggered and ClosingEvent as it
        // starts closing, ClosedEvent at the bottom.
        (float opened, _, _, _, _) = Assert.Single(d.Opened.Calls);
        V.Near(t0 + 1f, opened, 2 * Tick);
        Assert.Equal(2, d.Event.Calls.Count);
        Assert.True(d.Event.Calls[0].Trigger);
        Assert.False(d.Event.Calls[1].Trigger);
        V.Near(t0 + 5f, d.Event.Calls[1].Time, 2 * Tick);
        V.Near(t0 + 5f, Assert.Single(d.Closing.Calls).Time, 2 * Tick);
        V.Near(t0 + 6f, Assert.Single(d.Closed.Calls).Time, 3 * Tick);
        Assert.Equal(0f, d.M.Location.Z, 3);
        Assert.True(d.M.bClosed);
        Assert.Null(d.M.Instigator);
        Assert.True(d.M.AmbientSound.IsNone);
        foreach (string sound in new[] { "open", "opened", "close", "closed" })
        {
            SimEvent e = Assert.Single(d.S.Sounds.Of(SimEventKind.PlaySound, sound));
            Assert.Equal(228f / 255f, e.Volume);
            Assert.Equal(1f, e.Pitch);
        }
    }

    [Fact]
    public void ATriggerWhileMovingIsDropped()
    {
        Door d = MakeDoor();
        float t0 = Fire(d);
        d.S.Step(20);
        Fire(d);
        d.S.StepSeconds(2f);
        V.Near(t0 + 1f, Assert.Single(d.Opened.Calls).Time, 2 * Tick);
        Assert.Single(d.Opening.Calls);
    }

    [Fact]
    public void ATriggerWhileHoldingOpenRestartsTheOpenLabel()
    {
        Door d = MakeDoor();
        float t0 = Fire(d);
        d.S.StepSeconds(3f);
        float t1 = Fire(d);
        d.S.StepSeconds(6f);

        // A motionless leg of MoveTime (with the opening sound again), then a full new hold.
        Assert.Equal(2, d.Opening.Calls.Count);
        Assert.Equal(2, d.S.Sounds.Of(SimEventKind.PlaySound, "open").Count);
        Assert.Equal(2, d.Opened.Calls.Count);
        V.Near(t1 + 1f, d.Opened.Calls[1].Time, 2 * Tick);
        V.Near(t1 + 5f, Assert.Single(d.Closing.Calls).Time, 2 * Tick);
        Assert.True(t1 + 5f > t0 + 6f);
    }

    [Fact]
    public void AnUnTriggerWhileHoldingStopsTheCodeAndTheDoorStaysOpen()
    {
        Door d = MakeDoor();
        Fire(d);
        d.S.StepSeconds(2f);
        d.S.Level.UntriggerEvent("door", d.Switch, null);
        d.S.StepSeconds(10f);
        Assert.Empty(d.Closing.Calls);
        Assert.Equal(100f, d.M.Location.Z, 3);
        Assert.True(d.M.IsInState("TriggerOpenTimed"));
    }

    [Fact]
    public void SpecialBehaviorsPickTimesAndStatePerSender()
    {
        Door d = MakeDoor();
        Sender special = d.S.Spawn(new Sender());
        d.M.SpecialBehaviors.Add(new FSpecialBehavior
        {
            EventFromActor = special,
            InitialState = "TriggerToggle",
            MoveTime = 2f,
            StayOpenTime = 9f,
            MoverEncroachType = EMoverEncroachType.ME_CrushWhenEncroach,
        });
        d.M.eventTrigger(special, null);
        Assert.True(d.M.IsInState("TriggerToggle"));
        Assert.Equal(2f, d.M.MoveTime);
        Assert.Equal(EMoverEncroachType.ME_CrushWhenEncroach, d.M.MoverEncroachType);
        d.S.StepSeconds(3f);

        // Any other sender restores InitialState and the original times.
        d.M.eventTrigger(d.Switch, null);
        Assert.True(d.M.IsInState("TriggerOpenTimed"));
        Assert.Equal(1f, d.M.MoveTime);
        Assert.Equal(4f, d.M.StayOpenTime);
        Assert.Equal(EMoverEncroachType.ME_ReturnWhenEncroach, d.M.MoverEncroachType);
    }

    [Fact]
    public void IsTriggerableWhileMovingFollowsTheSenderAndTheOnceOnlyQuirk()
    {
        Door d = MakeDoor();
        Sender plain = d.S.Spawn(new Sender());
        ForcingSender forcing = d.S.Spawn(new ForcingSender());
        Assert.True(d.M.IsTriggerable(plain));
        d.M.InterpolateTo(1, 1f);
        Assert.False(d.M.IsTriggerable(plain));
        Assert.True(d.M.IsTriggerable(forcing));

        d.M.SpecialBehaviors.Add(new FSpecialBehavior { EventFromActor = plain, InitialState = "TriggerOpenOnceOnly" });
        Assert.True(d.M.IsTriggerable(plain));

        // An entry for the sender with another state does not stop the InitialState test (the loop never breaks).
        d.M.SpecialBehaviors[0] = new FSpecialBehavior { EventFromActor = plain, InitialState = "TriggerToggle" };
        d.M.InitialState = "TriggerCloseOnceOnly";
        Assert.True(d.M.IsTriggerable(plain));
        d.M.InitialState = "TriggerOpenTimed";
        Assert.False(d.M.IsTriggerable(plain));
    }

    [Fact]
    public void TriggerOpenOnceOnlyOpensOnceThenLocksForTheRound()
    {
        Door d = MakeDoor("TriggerOpenOnceOnly");
        float t0 = Fire(d);
        Assert.Equal(1, d.M.numTriggerEvents);
        Assert.NotEqual(0UL, d.M.DisabledProbes);
        d.S.Step(20);
        Fire(d);
        d.S.Level.UntriggerEvent("door", d.Switch, null);
        Assert.Equal(1, d.M.numTriggerEvents);
        d.S.StepSeconds(2f);
        V.Near(t0 + 1f, Assert.Single(d.Opened.Calls).Time, 2 * Tick);
        Assert.True(d.M.IsInState("s_TemporaryInactive"));
        Assert.Equal(0UL, d.M.DisabledProbes);
        Fire(d);
        d.S.StepSeconds(2f);
        Assert.Single(d.Opening.Calls);
        Assert.Equal(100f, d.M.Location.Z, 3);

        // UnLock returns to InitialState.
        d.M.eventUnLock();
        Assert.True(d.M.IsInState("TriggerOpenOnceOnly"));
    }

    [Fact]
    public void TriggerOpenOnceOnlyAlsoOpensOnAnUnTrigger()
    {
        Door d = MakeDoor("TriggerOpenOnceOnly");
        d.S.Level.UntriggerEvent("door", d.Switch, null);
        d.S.StepSeconds(1.2f);
        Assert.Single(d.Opened.Calls);
    }

    [Fact]
    public void LockDuringASequenceIsTakenWhenItFinishes()
    {
        Door d = MakeDoor();
        Fire(d);
        d.S.Step(10);
        d.M.eventLock();
        Assert.True(d.M.bShouldLock);
        Assert.True(d.M.IsInState("TriggerOpenTimed"));
        d.S.StepSeconds(1f);
        Assert.True(d.M.IsInState("s_TemporaryInactive"));
        Assert.False(d.M.bShouldLock);
        d.S.StepSeconds(6f);
        Assert.Empty(d.Closing.Calls);
    }

    [Fact]
    public void TriggerToggleOpensThenClosesOnTheNextTrigger()
    {
        Door d = MakeDoor("TriggerToggle");
        Fire(d);
        d.S.StepSeconds(1.5f);
        Assert.Equal(100f, d.M.Location.Z, 3);
        Fire(d);
        d.S.StepSeconds(1.5f);
        Assert.Equal(0f, d.M.Location.Z, 3);
        Assert.Single(d.Closed.Calls);
    }

    [Fact]
    public void TriggerControlStaysOpenUntilTheLastRelease()
    {
        Door d = MakeDoor("TriggerControl");
        d.M.eventTrigger(d.Switch, null);
        d.M.eventTrigger(d.Switch, null);
        d.S.StepSeconds(1.5f);
        Assert.Equal(2, d.M.numTriggerEvents);
        d.S.Level.UntriggerEvent("door", d.Switch, null);
        d.S.StepSeconds(1.5f);
        Assert.Equal(100f, d.M.Location.Z, 3);
        d.S.Level.UntriggerEvent("door", d.Switch, null);
        d.S.StepSeconds(1.5f);
        Assert.Equal(0f, d.M.Location.Z, 3);
        Assert.Equal(0, d.M.numTriggerEvents);
    }

    [Fact]
    public void TriggerPoundCyclesWhileTriggeredAndStopsWhenReleased()
    {
        Door d = MakeDoor("TriggerPound", moveTime: 0.5f, stayOpen: 0.5f, setup: m => m.OtherTime = 0.5f);
        Fire(d);
        d.S.StepSeconds(4.5f);
        Assert.True(d.Opening.Calls.Count >= 2);
        d.S.StepUntil(() => !d.M.bInterpolating);
        d.S.Level.UntriggerEvent("door", d.Switch, null);
        d.S.StepSeconds(3f);
        Assert.Equal(0f, d.M.Location.Z, 3);
        int openings = d.Opening.Calls.Count;
        d.S.StepSeconds(3f);
        Assert.Equal(openings, d.Opening.Calls.Count);
        Assert.Empty(d.Opened.Calls);
    }

    [Fact]
    public void TriggerCloseTimedClosesAnOpenMoverHoldsAndReopens()
    {
        Door d = MakeDoor("TriggerCloseTimed", setup: m => m.KeyNum = 1);
        Assert.Equal(100f, d.M.Location.Z, 3);
        float t0 = Fire(d);
        d.S.StepSeconds(1.5f);
        Assert.Equal(0f, d.M.Location.Z, 3);
        V.Near(t0 + 1f, Assert.Single(d.Closed.Calls).Time, 2 * Tick);
        d.S.StepSeconds(5f);
        Assert.Equal(100f, d.M.Location.Z, 3);
        Assert.Single(d.Opened.Calls);
    }

    [Fact]
    public void BumpOpenTimedOpensForAnAcceptedBumperAndIgnoresBumpsWhileCycling()
    {
        Door d = MakeDoor("BumpOpenTimed", setup: m =>
        {
            m.BumpEvent = "bump";
            m.PlayerBumpEvent = "playerbump";
        });
        EventProbe bump = d.S.Spawn(new EventProbe(), tag: "bump");
        EventProbe playerBump = d.S.Spawn(new EventProbe(), tag: "playerbump");
        TestMerc stranger = d.S.Spawn(new TestMerc());
        d.M.eventBump(stranger);
        Assert.False(d.M.bInterpolating);
        d.S.Step();
        Assert.Empty(d.Opening.Calls);

        TestMerc player = d.S.Player<TestMerc>(default);
        d.M.eventBump(player);
        d.S.Step();
        Assert.Single(d.Opening.Calls);
        Assert.Same(player, d.M.Instigator);
        Assert.Single(bump.Calls);
        Assert.Single(playerBump.Calls);
        d.M.eventBump(player);
        Assert.Single(bump.Calls);
    }

    [Fact]
    public void StandOpenTimedOpensWhenAPawnStartsStandingOnIt()
    {
        Door d = MakeDoor("StandOpenTimed", setup: m => m.BumpType = EBumpType.BT_AnyBump);
        Sender crate = d.S.Spawn(new Sender());
        crate.SetBase(d.M);
        d.S.Step();
        Assert.Single(d.Opening.Calls);
    }

    [Fact]
    public void BumpButtonStaysDownWhileItsReceiverWorks()
    {
        DeviceScene s = DeviceScene.Create();
        Mover button = s.Add(new Mover { InitialState = "BumpButton", Event = "door", BumpType = EBumpType.BT_AnyBump }, default);
        button.KeyPos[1] = V.At(0f, 0f, -5f);
        Mover door = s.Add(new Mover { InitialState = "TriggerOpenTimed", StayOpenTime = 2f }, V.At(500f, 0f, 0f), tag: "door");
        door.BasePos = V.At(500f, 0f, 0f);
        door.KeyPos[1] = V.At(0f, 0f, 100f);
        s.Begin();
        Sender presser = s.Spawn(new Sender());
        button.eventBump(presser);
        s.StepSeconds(1.5f);

        // The door took the button as its saved trigger (BeginEvent): the button holds down.
        Assert.True(button.bSlave);
        Assert.Equal(-5f, button.Location.Z, 3);
        s.StepSeconds(2.5f);
        Assert.Equal(-5f, button.Location.Z, 3);

        // The door closing (EndEvent) releases the button.
        s.StepSeconds(3f);
        Assert.False(button.bSlave);
        Assert.Equal(0f, button.Location.Z, 3);
    }

    [Fact]
    public void DamageTriggersADamageTriggeredMoverAtTheThreshold()
    {
        Door d = MakeDoor(setup: m =>
        {
            m.bDamageTriggered = true;
            m.DamageThreshold = 20f;
        });
        TestMerc shooter = d.S.Player<TestMerc>(default);
        d.M.eventTakeDamage(19, shooter, default, default, SDamageType.SDTBase);
        d.S.Step();
        Assert.Empty(d.Opening.Calls);
        d.M.eventTakeDamage(20, shooter, default, default, SDamageType.SDTBase);
        d.S.Step();
        Assert.Single(d.Opening.Calls);
        Assert.Same(shooter, d.M.Instigator);
    }

    [Fact]
    public void ServersKeepRealPositionAndClientsSnapBackWhenFarOff()
    {
        Door server = MakeDoor(mode: ENetMode.NM_DedicatedServer);
        Fire(server);
        server.S.StepSeconds(2f);
        Assert.Equal(server.M.Location, server.M.RealPosition);

        Door client = MakeDoor(mode: ENetMode.NM_Client, setup: m => m.Role = ENetRole.ROLE_SimulatedProxy);
        client.M.RealPosition = V.At(0f, 0f, 50f);
        client.S.StepSeconds(4.05f);
        Assert.True(client.M.bClientPause);
        client.S.StepSeconds(4f);
        Assert.False(client.M.bClientPause);
        Assert.Equal(V.At(0f, 0f, 50f), client.M.Location);
    }

    [Fact]
    public void SlaveMoversWithTheSameTagRideTheirMaster()
    {
        DeviceScene s = DeviceScene.Create();
        Mover master = s.Add(new Mover { InitialState = "TriggerOpenTimed" }, default, tag: "lift");
        master.KeyPos[1] = V.At(0f, 0f, 100f);
        Mover slave = s.Add(new Mover { InitialState = "TriggerOpenTimed", bSlave = true, BasePos = V.At(0f, 0f, 10f) }, V.At(0f, 0f, 10f), tag: "lift");
        s.Begin();
        Assert.Same(master, slave.Base);
        master.eventTrigger(null, null);
        s.StepSeconds(1.5f);
        Assert.Equal(110f, slave.Location.Z, 3);
    }

    [Fact]
    public void AReturnGroupLeaderOpensItsFollowers()
    {
        DeviceScene s = DeviceScene.Create();
        Mover leader = s.Add(new Mover { InitialState = "TriggerOpenTimed", ReturnGroup = "g", bIsLeader = true }, default, tag: "group");
        leader.KeyPos[1] = V.At(0f, 0f, 100f);
        Mover follower = s.Add(new Mover { InitialState = "TriggerOpenTimed", ReturnGroup = "g", BasePos = V.At(300f, 0f, 0f) }, V.At(300f, 0f, 0f));
        follower.KeyPos[1] = V.At(0f, 0f, 50f);
        s.Begin();
        Assert.Same(leader, follower.Leader);
        Assert.Same(follower, leader.Follower);
        leader.eventTrigger(null, null);
        s.StepSeconds(1.5f);
        Assert.Equal(50f, follower.Location.Z, 3);
    }
}
