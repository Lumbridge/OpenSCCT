using System.Collections.Generic;
using System.Text;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class DoorAiTests
{
    [Theory]
    [InlineData(false, false, 0, false, PawnTeam.Merc)]
    [InlineData(true, false, 0, false, PawnTeam.Merc)]
    [InlineData(true, true, 0, false, PawnTeam.Merc)]
    [InlineData(true, false, 10, false, PawnTeam.Merc)]
    [InlineData(true, true, 0, true, PawnTeam.Merc)]
    [InlineData(true, true, 0, false, PawnTeam.Spy)]
    [InlineData(true, true, 0, true, PawnTeam.Spy)]
    public void BotUsesDoorButtonOnceAndWaitsForItsDelayedEvent(bool delayed, bool closedAtKeyOne, int reuseSeconds,
        bool crampedUseSpot, PawnTeam team)
    {
        var fixture = ButtonDoor(delayed, closedAtKeyOne, reuseSeconds, crampedUseSpot, team);
        (PawnRig rig, Mover door, SInterrupteur button, SMagicEvent? relay, DoorEventProbe presses,
            DoorEventProbe openings, AiWorld world, DoorAi doors, NavFollower route) = fixture;
        if (crampedUseSpot)
        {
            // The authored standing spot overlaps the wall with a merc's body, but the button's interaction
            // cylinder also reaches a navigable sample farther from the wall.
            button.GetUseSpot(rig.Pawn, out FVector authored, out _);
            Assert.False(new NavFollower(world, spy: team == PawnTeam.Spy).SetGoal(rig.Pawn, V(authored.X, authored.Y, 0f)));
        }

        var trace = new StringBuilder();
        string previous = string.Empty;
        for (int frame = 0; frame < 30 * Sim.Hz && !route.Arrived; frame++)
        {
            var plan = new AiPlan();
            bool handling = doors.Steer(world.Now, ref plan);
            if (!handling)
            {
                NavCommand command = route.Steer(rig.Pawn, world.Now, rig.Controller.Rotation.Yaw & 0xFFFF, sneak: false);
                plan.Keys = command.Keys;
                plan.Yaw = command.Yaw;
            }

            string state = $"{doors.Status} {rig.Controller.GetStateName()} key={door.KeyNum} uses={presses.Times.Count}";
            if (state != previous)
            {
                trace.AppendLine($"{world.Now:F2}: {state}, pawn={rig.Pawn.Location}");
                previous = state;
            }

            StepPlan(rig, plan);
        }

        Assert.True(route.Arrived, trace.ToString());
        Assert.True(rig.Pawn.Location.Y > 100f, trace.ToString());
        float pressedAt = Assert.Single(presses.Times);
        float openedAt = Assert.Single(openings.Times);
        Assert.True(openedAt - pressedAt >= (delayed ? 9.99f : 0f), trace.ToString());
        Assert.True(pressedAt >= reuseSeconds, trace.ToString());
        Assert.Equal(closedAtKeyOne ? 0 : 1, door.KeyNum);
        Assert.True(button.TriggerInteraction!.iLastUsedTime >= reuseSeconds);

        // Repeated Use during the wait would queue another toggle that closes the exit behind the first one.
        for (int frame = 0; frame < 12 * Sim.Hz; frame++) rig.Step(default);
        Assert.Single(presses.Times);
        Assert.Single(openings.Times);
        if (relay is not null) Assert.Empty(relay.EventQueue);
        Assert.Equal(closedAtKeyOne ? 0 : 1, door.KeyNum);
    }

    [Fact]
    public void MercWaitsForAnEscapeAlreadyQueuedByAnotherPlayer()
    {
        var (rig, door, button, relay, presses, openings, world, doors, route) = ButtonDoor(delayed: true);
        // Another player's accepted button event is pending before this bot reaches the blocked exit.
        relay!.eventTrigger(button, null);
        var trace = new StringBuilder();
        for (int frame = 0; frame < 20 * Sim.Hz && !route.Arrived; frame++)
        {
            var plan = new AiPlan();
            if (!doors.Steer(world.Now, ref plan))
            {
                NavCommand command = route.Steer(rig.Pawn, world.Now, rig.Controller.Rotation.Yaw & 0xFFFF, sneak: false);
                plan.Keys = command.Keys;
                plan.Yaw = command.Yaw;
            }

            if (frame % Sim.Hz == 0) trace.AppendLine($"{world.Now:F2}: {doors.Status}, {rig.Pawn.Location}");
            StepPlan(rig, plan);
        }

        Assert.True(route.Arrived, trace.ToString());
        Assert.Empty(presses.Times);
        Assert.Single(openings.Times);
        Assert.Empty(relay.EventQueue);
        Assert.Equal(1, door.KeyNum);
    }

    [Fact]
    public void MercReleasesTheRouteWhenDoorMotionStalls()
    {
        var (rig, door, _, _, presses, _, world, doors, _) = ButtonDoor(delayed: false);
        // An encroached mover can retain interpolation without clearing the doorway. Keep the geometry still
        // while advancing ordinary world ticks, so the bot must eventually return control to navigation.
        door.bInterpolating = true;
        door.Physics = EPhysics.PHYS_None;
        var plan = new AiPlan();
        Assert.True(doors.Steer(world.Now, ref plan));
        float startedAt = world.Now;
        bool released = false;
        for (int frame = 0; frame < 6 * Sim.Hz; frame++)
        {
            rig.Step(default);
            plan = new AiPlan();
            if (!doors.Steer(world.Now, ref plan))
            {
                released = true;
                break;
            }
        }

        Assert.True(released);
        Assert.False(doors.Busy);
        Assert.InRange(world.Now - startedAt, 4f, 5f);
        Assert.True(door.bInterpolating);
        Assert.Equal(0, door.KeyNum);
        Assert.Empty(presses.Times);
    }

    [Theory]
    [InlineData(-1, EEventType.EVT_Trigger)]
    [InlineData(0, EEventType.EVT_Untrigger)]
    public void MercDoesNotUseAButtonWhoseRelayCannotRespond(int repeat, EEventType validOn)
    {
        var (rig, _, _, relay, presses, _, world, doors, _) = ButtonDoor(delayed: true);
        FEventGroup group = relay!.Groups[0];
        group.Repeat = repeat;
        FSuperEvent entry = group.EventGroup[0];
        entry.ValidOn = validOn;
        group.EventGroup[0] = entry;
        relay.Groups[0] = group;
        var plan = new AiPlan();

        Assert.False(doors.Steer(world.Now, ref plan));
        Assert.False(doors.Busy);
        Assert.NotEmpty(world.BadEdges(false));
        Assert.Empty(presses.Times);
        Assert.Equal(0, plan.Keys.bUsing);
    }

    private static (PawnRig Rig, Mover Door, SInterrupteur Button, SMagicEvent? Relay, DoorEventProbe Presses,
        DoorEventProbe Openings, AiWorld World, DoorAi Doors, NavFollower Route) ButtonDoor(
        bool delayed, bool closedAtKeyOne = false, int reuseSeconds = 0, bool crampedUseSpot = false,
        PawnTeam team = PawnTeam.Merc)
    {
        var level = Floor();
        if (crampedUseSpot) level.AddBox(V(-300f, -320f, 0f), V(300f, -225f, 256f));
        level.AddBoxHullCollider(ColliderKind.Mover, "Door", CollisionDataBuilder.WorldFlags,
            V(-300f, -8f, 0f), V(300f, 8f, 256f));
        PawnRig rig = Rig(level, null, V(0f, -160f, 0f), team);
        var mover = new Mover
        {
            bStatic = false,
            bNoDelete = false,
            InitialState = "TriggerToggle",
            Tag = "WaterproofDoor",
            MoveTime = 1f,
            KeyNum = closedAtKeyOne ? (byte)1 : (byte)0,
            BasePos = closedAtKeyOne ? V(0f, 0f, 320f) : default,
        };
        mover.KeyPos[1] = V(0f, 0f, closedAtKeyOne ? -320f : 320f);
        Mover door = rig.Level.SpawnActor(mover, default, default)!;
        Name buttonEvent = delayed ? new Name("InteriorButton") : door.Tag;
        SInterrupteur button = rig.Level.SpawnActor(new SInterrupteur
        {
            bStatic = false,
            bNoDelete = false,
            Event = buttonEvent,
            UsableBy = Usable.U_BOTH,
            UseRelativeLocation = V(crampedUseSpot ? 10f : 40f, 0f, 0f),
            fInteractionRadius = 60f,
            fInteractionHeight = 120f,
            bInteractInFront = true,
            iReusableTime = reuseSeconds,
        }, V(0f, -220f, 0f), new FRotator(0, 16384, 0))!;
        SMagicEvent? relay = null;
        if (delayed)
        {
            var group = new FEventGroup();
            group.EventGroup.Add(new FSuperEvent { Event = door.Tag, Delay = 10f });
            relay = rig.Level.SpawnActor(new SMagicEvent
            {
                bStatic = false,
                bNoDelete = false,
                Tag = buttonEvent,
                Groups = new List<FEventGroup> { group },
            }, default, default)!;
        }

        DoorEventProbe presses = rig.Level.SpawnActor(new DoorEventProbe { Tag = buttonEvent }, default, default)!;
        DoorEventProbe openings = delayed
            ? rig.Level.SpawnActor(new DoorEventProbe { Tag = door.Tag }, default, default)!
            : presses;
        rig.Collision!.BindLevel(rig.Level, id => id == "Door" ? door : null);
        AiWorld world = World(rig, V(-256f, -320f, -100f), V(256f, 320f, 400f));
        var bot = new AiController(world, rig.Controller, AiDifficulty.For(AiSkill.Normal), 0);
        bot.Tick();
        NavFollower route = bot.Brain!.Follower;
        Assert.True(route.SetGoal(rig.Pawn, V(0f, 160f, 0f)));
        Assert.NotNull(button.TriggerInteraction);
        return (rig, door, button, relay, presses, openings, world, new DoorAi(bot, route, spy: team == PawnTeam.Spy), route);
    }

    private static void StepPlan(PawnRig rig, AiPlan plan) => rig.Step(new PlayerInputFrame
    {
        BT_Up = plan.Keys.BT_Up,
        BT_Down = plan.Keys.BT_Down,
        BT_Left = plan.Keys.BT_Left,
        BT_Right = plan.Keys.BT_Right,
        bUsing = plan.Keys.bUsing,
        FastSpeed = plan.Keys.FastSpeed,
        View = plan.Yaw is { } yaw ? new FRotator(plan.Pitch ?? 0, yaw, 0) : null,
    });

    private sealed class DoorEventProbe : Actor
    {
        public List<float> Times { get; } = new();

        protected override void Trigger(Actor? Other, Pawn? EventInstigator) => Times.Add(Level.TimeSeconds);
    }

    [Theory]
    [InlineData(PawnTeam.Merc, "SBase.SPAWNDEFENSE", true, false, true)]
    [InlineData(PawnTeam.Merc, "SBase.SBasePawn", true, false, true)]
    [InlineData(PawnTeam.Spy, "SBase.SPAWNDEFENSE", true, false, false)]
    [InlineData(PawnTeam.Merc, "SBase.SPAWNDEFENSE", false, false, false)]
    [InlineData(PawnTeam.Merc, "SBase.SPAWNDEFENSE", true, true, false)]
    [InlineData(PawnTeam.Merc, "None", true, false, false)]
    public void PresenceSensorDoorIsOnlyKeptOnRoutesForPawnsThatCanOpenIt(
        PawnTeam team, string detectedClass, bool enabled, bool untrigger, bool permitted)
    {
        var level = Floor();
        level.AddBoxHullCollider(ColliderKind.Mover, "Door", CollisionDataBuilder.WorldFlags,
            V(-300f, -8f, 0f), V(300f, 8f, 256f));
        PawnRig rig = Rig(level, null, V(0f, -160f, 0f), team);
        Mover door = rig.Level.SpawnActor(new Mover
        {
            bStatic = false,
            bNoDelete = false,
            InitialState = new Name("TriggerOpenTimed"),
            Tag = new Name("SpawnDoor"),
        }, default, default)!;
        Assert.NotNull(door);
        SPresenceTrigger sensor = rig.Level.SpawnActor(new SPresenceTrigger
        {
            bStatic = false,
            bNoDelete = false,
            Event = door.Tag,
            DetectedClass = new ClassRef(detectedClass),
            bCollideActors = enabled,
            bUntrigger = untrigger,
            CollisionRadius = 40f,
            CollisionHeight = 90f,
        }, V(0f, -40f, 90f), default)!;
        Assert.NotNull(sensor);
        rig.Collision!.BindLevel(rig.Level, id => id == "Door" ? door : null);
        AiWorld world = World(rig, V(-256f, -320f, -100f), V(256f, 320f, 400f));
        var bot = new AiController(world, rig.Controller, AiDifficulty.For(AiSkill.Normal), 0);
        bot.Tick();
        AiBrain brain = bot.Brain!;
        Assert.True(brain.Follower.SetGoal(rig.Pawn, V(0f, 160f, 0f)));
        var plan = new AiPlan();
        new DoorAi(bot, brain.Follower, team == PawnTeam.Spy).Steer(world.Now, ref plan);

        // The bot sees the closed mover before entering the sensor. Allowed pawns must keep approaching it;
        // others must mark the crossing blocked instead of assuming a sensor of the wrong class will open it.
        if (permitted)
        {
            Assert.Empty(world.BadEdges(team == PawnTeam.Spy));
            Assert.False(brain.Follower.Failed);
        }
        else
        {
            Assert.NotEmpty(world.BadEdges(team == PawnTeam.Spy));
        }
    }
}
