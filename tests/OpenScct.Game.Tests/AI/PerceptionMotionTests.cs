using System;
using System.Linq;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Game.Tests.AI;

/// <summary>
/// Bots and the merc's motion tracker (spec/equipment/motion-tracking.md): a merc bot in motion vision sees a spy only
/// through its own goggles' tracker (the game's scan: 150 uu/s, 2600 uu, a clear line), and a spy bot plays around the
/// rule by its difficulty (Evasion.cs).
/// </summary>
[Collection(AiMatchCollection.Name)]
public sealed class PerceptionMotionTests
{
    private static readonly MapCatalog Maps = MapCatalog.CreateDefault();

    private sealed class Scene : IDisposable
    {
        public required AiBotMatch Match;
        public required AiController Merc;
        public required AiController Spy;
        public AiInput SpyInput;
        public FVector SpySpot;
        public bool DriveSpy;

        public void Dispose() => Match.Dispose();

        public void Step(float seconds)
        {
            long until = Match.Frame + (long)(seconds * Sim.Hz);
            while (Match.Frame < until)
            {
                Match.Step();
            }
        }
    }

    /// <summary>
    /// TestBox with a merc bot and a spy bot held still (no brain), the spy placed in full view <paramref name="distance"/>
    /// in front of the merc on a clear line, both standing; the spy's keys are written after the bots' own each tick
    /// while <see cref="Scene.DriveSpy"/> is set.
    /// </summary>
    private static Scene? Start(float distance, AiDifficulty? spy = null, AiDifficulty? merc = null)
    {
        if (Maps.FindFolder(MapCatalog.TestBox) is null)
        {
            return null;
        }

        // A merc that never switches vision itself (gadget use under 0.15), so the test's motion vision stays on.
        AiDifficulty quiet = (merc ?? AiDifficulty.For(AiSkill.Normal)) with { GadgetUse = 0.1f };
        var match = new AiBotMatch(new AiBotMatchSettings
        {
            Map = MapCatalog.TestBox,
            Bots = [new ServerBotSpec("SpyBot1", 0, spy ?? AiDifficulty.For(AiSkill.Normal)), new ServerBotSpec("MercBot1", 1, quiet)],
            Seed = 7,
            Classes = NetClasses.Create(),
            Maps = Maps,
        });
        AiController? m = null;
        AiController? s = null;
        while (match.Frame < 20 * Sim.Hz && (m?.Pawn is null || s?.Pawn is null))
        {
            match.Step();
            m = match.Bots.Bots.FirstOrDefault(b => b.Pawn is SPAWNDEFENSE);
            s = match.Bots.Bots.FirstOrDefault(b => b.Pawn is SPawnAttaque);
        }

        Assert.NotNull(m);
        Assert.NotNull(s);
        var scene = new Scene { Match = match, Merc = m!, Spy = s! };
        m!.Held = true;
        s!.Held = true;
        match.Host.BeforeWorldTick += _ =>
        {
            if (scene.DriveSpy)
            {
                scene.SpyInput.ApplyTo(scene.Spy.Controller);
            }
        };
        // Both players walking (the round started), then placed; a lock from the placement's fall runs out.
        for (int i = 0; i < 30 && !(Walking(scene.Merc) && Walking(scene.Spy)); i++)
        {
            scene.Step(0.5f);
        }

        Assert.True(Walking(scene.Merc) && Walking(scene.Spy),
            $"the players are not walking: {scene.Merc.Controller.GetStateName()} {scene.Spy.Controller.GetStateName()}");
        Place(scene, distance);

        // The spy spawns crouched: stand it up with the crouch key, as a player does.
        if (scene.Spy.Pawn!.bIsCrouched)
        {
            scene.SpyInput = new AiInput { bCrouch = 1 };
            scene.DriveSpy = true;
            scene.Step(2f / Sim.Hz);
            scene.SpyInput = default;
            scene.Step(1f);
            scene.DriveSpy = false;
        }

        Assert.False(scene.Spy.Pawn!.bIsCrouched, "the spy did not stand up");
        return scene;
    }

    /// <summary>
    /// Puts the merc on an open spot and the spy <paramref name="distance"/> in front of it along the first clear
    /// direction (room to run 400 uu on), and turns both views: the merc's on the spy, the spy's across.
    /// </summary>
    private static void Place(Scene s, float distance)
    {
        SBasePawn merc = s.Merc.Pawn!;
        SBasePawn spy = s.Spy.Pawn!;
        AiWorld world = s.Merc.World;
        FVector start = merc.Location;
        var why = new System.Text.StringBuilder();
        var spots = new System.Collections.Generic.List<FVector> { start };
        for (int gx = -4; gx <= 4; gx++)
        {
            for (int gy = -4; gy <= 4; gy++)
            {
                spots.Add(new FVector(start.X + gx * 256f, start.Y + gy * 256f, start.Z));
            }
        }

        foreach (FVector spot in spots)
        {
            if (!merc.SetLocation(spot))
            {
                continue;
            }

            for (int k = 0; k < 16; k++)
            {
                FVector dir = new FRotator(0, k * 4096, 0).Vector();
                FVector side = new FRotator(0, (k * 4096 + 16384) & 0xFFFF, 0).Vector();
                FVector at = spot + dir * distance;
                FVector down = new(0f, 0f, 200f);
                if (!world.LineOpen(spot, at) || !world.LineOpen(at, at + side * 450f) || !world.LineOpen(spot, at + side * 450f) ||
                    world.LineOpen(spot, spot - down) || world.LineOpen(at, at - down) || world.LineOpen(at + side * 450f, at + side * 450f - down))
                {
                    why.Append('.');
                    continue;
                }

                if (!spy.SetLocation(at))
                {
                    continue;
                }

                s.SpySpot = at;

                s.Merc.Controller.SetViewRotation(new FRotator(0, k * 4096, 0));
                s.Spy.Controller.SetViewRotation(new FRotator(0, (k * 4096 + 16384) & 0xFFFF, 0));
                var trace = new System.Text.StringBuilder();
                for (int i = 0; i < 20 && spy.Physics != EPhysics.PHYS_Walking; i++)
                {
                    s.Step(0.25f);
                    trace.Append(spy.Location).Append(' ').Append(spy.Physics).Append(' ').Append(s.Spy.Controller.GetStateName()).Append("; ");
                }

                Assert.True(spy.Physics == EPhysics.PHYS_Walking, "the placed spy did not land: " + trace);
                return;
            }
        }

        Assert.Fail($"no clear spot for the spy in front of the merc (from {start}): {why}");
    }

    private static bool Walking(AiController b) => b.Controller.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal);

    private static void MotionOn(Scene s)
    {
        s.Merc.Controller.eventSwitchHeadset(1);
        s.Step(1.2f);
        if (s.Merc.Controller.RendMap != SGoggle.REN_MotionTrackingVision && s.Merc.Pawn is SBasePawn merc)
        {
            // Without the converted character clips (a checkout without .content) the goggle clip has no notifies,
            // so the switch the clip's notify makes is done here, as the clip would.
            if (merc.bGogglesUp)
            {
                PawnVision.AnimEvent_GoggleSwitchDown(merc);
            }
            else
            {
                PawnVision.AnimEvent_GoggleSwitchUp(merc);
            }

            merc.PawnAnim.Stop(SBasePawn.GOGGLECHANNEL);
            merc.eventAnimEnd(SBasePawn.GOGGLECHANNEL);
            s.Step(0.6f);
        }

        Assert.Equal(SGoggle.REN_MotionTrackingVision, s.Merc.Controller.RendMap);
        Assert.NotNull(AiMotionSense.Tracker(s.Merc.Controller));
    }

    private static bool Shown(Scene s)
    {
        SPlayerController pc = s.Merc.Controller;
        return s.Merc.Motion.Look(pc, AiSight.Eye(s.Merc.Pawn!), pc.Rotation, pc.FovAngle, s.Spy.Pawn!).Visible;
    }

    private static float Speed(Pawn p) => SvmMath.Sqrt(p.Velocity.X * p.Velocity.X + p.Velocity.Y * p.Velocity.Y);

    [Fact]
    public void MotionVisionShowsTheLitBodyWhileItsTrackerOnlyLocksFastMovement()
    {
        using Scene? s = Start(800f);
        Assert.SkipWhen(s is null, "The TestBox fixture is not in this checkout.");
        MotionOn(s!);
        SMotionTrackingVision tracker = AiMotionSense.Tracker(s!.Merc.Controller)!;
        var life = new System.Text.StringBuilder();
        for (int i = 0; i < 12; i++)
        {
            s.Step(0.25f);
            life.Append(s.Spy.Controller.GetStateName()).Append(' ').Append(s.Spy.Pawn?.Health).Append(' ').Append(s.Spy.Pawn?.Location).Append("; ");
        }

        Assert.True(Walking(s.Spy), "the spy left its walk: " + life);

        // Standing still in full view: no tracker bracket, but the normally lit body still appears in the scene.
        s.Step(1.5f);
        Assert.Equal(-1, tracker.FindLock(s.Spy.Pawn!));
        Assert.False(Shown(s));
        Assert.True(AiSight.Look(s.Merc.World, AiSight.Eye(s.Merc.Pawn!), s.Merc.Controller.Rotation, s.Merc.Controller.FovAngle,
            AiVisionMode.Motion, s.Spy.Pawn!, s.Merc.Difficulty).Visible);

        // Running across (423.5 uu/s): the next scan locks it and the bracket shows.
        s.SpyInput = new AiInput { BT_Up = 1, FastSpeed = true };
        s.DriveSpy = true;
        s.Step(0.8f);
        Assert.True(Speed(s.Spy.Pawn!) > 400f, $"the spy runs at {Speed(s.Spy.Pawn!)}: state {s.Spy.Controller.GetStateName()} BT_Up {s.Spy.Controller.BT_Up} " +
            $"accel {s.Spy.Pawn!.Acceleration} crouched {s.Spy.Pawn.bIsCrouched} phys {s.Spy.Pawn.Physics} aForward {s.Spy.Controller.aForward} player {s.Spy.Controller.Player?.GetType().Name}");
        Assert.True(tracker.FindLock(s.Spy.Pawn!) >= 0, "the running spy was not locked");
        Assert.True(Shown(s));

        // The merc bot, let go for a moment, takes the bracket as a sighting.
        s.Merc.Held = false;
        s.Step(0.2f);
        s.Merc.Held = true;
        AiContact? c = s.Merc.Memory.Find(s.Spy.Pawn!);
        Assert.True(c is { Spot: > 0f }, "the merc bot did not see the locked spy");

        // Creeping (crouched at the slow speed, 88.5 uu/s) from where it stood: the lock runs out (1.5 s hold, 0.25 s
        // fade, a scan) and no new one comes.
        s.SpyInput = default;
        s.Step(0.2f);
        Assert.True(s.Spy.Pawn!.SetLocation(s.SpySpot));
        s.SpyInput = new AiInput { BT_Up = 1, bCrouch = 1, FastSpeed = false };
        s.Step(0.05f);
        s.SpyInput = new AiInput { BT_Up = 1, FastSpeed = false };
        s.Step(3f);
        Assert.True(s.Spy.Pawn!.bIsCrouched, "the spy did not crouch");
        Assert.InRange(Speed(s.Spy.Pawn!), 80f, 100f);
        Assert.Equal(-1, tracker.FindLock(s.Spy.Pawn!));
        Assert.False(Shown(s));
    }

    [Fact]
    public void ACloakedSpyIsNeverLockedEvenAtTrackedSpeeds()
    {
        using Scene? s = Start(800f);
        Assert.SkipWhen(s is null, "The TestBox fixture is not in this checkout.");
        MotionOn(s!);
        SMotionTrackingVision tracker = AiMotionSense.Tracker(s!.Merc.Controller)!;
        s.Spy.Pawn!.DesiredGlassEffect = SMotionTrackingVisionK.CloakedGlass;

        // Standing at the slow speed (169.4 uu/s) is tracked when visible, not when cloaked (real game capture).
        s.SpyInput = new AiInput { BT_Up = 1, FastSpeed = false };
        s.DriveSpy = true;
        s.Step(1.5f);
        Assert.True(Speed(s.Spy.Pawn!) > 150f, $"the spy walks at {Speed(s.Spy.Pawn!)}");
        Assert.Equal(-1, tracker.FindLock(s.Spy.Pawn!));
        s.Spy.Pawn.DesiredGlassEffect = 0;
        s.Step(1f);
        Assert.True(tracker.FindLock(s.Spy.Pawn!) >= 0);
    }

    // ---- spy evasion ----------------------------------------------------------------------------------------------

    private static AiPlan Walk() => new() { Keys = new AiInput { BT_Up = 1, FastSpeed = true }, Tolerance = 9000 };

    /// <summary>The spy bot knows the merc (seen, sure) where it stands.</summary>
    private static void Knows(Scene s)
    {
        s.Spy.Memory.Observe(s.Merc.Pawn!, new AiSighting(true, 1f, 900f, 1f, true), s.Spy.World.Now, 2f,
            s.Spy.Difficulty);
    }

    [Theory]
    [InlineData(AiSkill.Easy, SpyEvasionAct.None)]
    [InlineData(AiSkill.Normal, SpyEvasionAct.Creep)]
    [InlineData(AiSkill.Hard, SpyEvasionAct.Freeze)]
    [InlineData(AiSkill.Expert, SpyEvasionAct.Freeze)]
    public void ASpyBotUnderAMercsEyesCreepsOrKeepsStillByDifficulty(AiSkill skill, SpyEvasionAct expected)
    {
        using Scene? s = Start(900f, spy: AiDifficulty.For(skill));
        Assert.SkipWhen(s is null, "The TestBox fixture is not in this checkout.");
        Knows(s!);
        s!.Spy.World.Light = Dark;
        // The merc faces the spy (Place turned its view and pawn toward it).
        s.Merc.Pawn!.Rotation = s.Merc.Controller.Rotation;
        AiPlan plan = Walk();
        s.Spy.Evasion.Apply(ref plan, s.Spy.World.Now);
        bool threat = s.Spy.Evasion.Threatened(s.Spy.Pawn!, out bool looking);
        Assert.True(expected == s.Spy.Evasion.Last,
            $"{expected} != {s.Spy.Evasion.Last}: threat {threat} looking {looking} spy {s.Spy.Pawn!.Location} {s.Spy.Pawn.Physics} merc {s.Merc.Pawn!.Location} " +
            $"yaw {s.Merc.Pawn.Rotation.Yaw} contacts {s.Spy.Memory.Contacts.Count} pawn {s.Spy.Pawn.GetType().Name} {s.Merc.Pawn.GetType().Name}");
        switch (expected)
        {
            case SpyEvasionAct.None:
                Assert.True(plan.Keys.FastSpeed);
                Assert.Equal(1, plan.Keys.BT_Up);
                break;
            case SpyEvasionAct.Creep:
                Assert.False(plan.Keys.FastSpeed);
                Assert.True(plan.Crouched);
                Assert.Equal(1, plan.Keys.BT_Up);
                break;
            default:
                Assert.False(plan.Keys.Moving);
                break;
        }
    }

    [Fact]
    public void ASpyBotCreepsWhenTheMercLooksAwayAndWalksFreelyOutOfItsRange()
    {
        using Scene? s = Start(900f, spy: AiDifficulty.For(AiSkill.Hard));
        Assert.SkipWhen(s is null, "The TestBox fixture is not in this checkout.");
        Knows(s!);
        s!.Spy.World.Light = Dark;
        // Facing away: creep, do not freeze.
        s.Merc.Pawn!.Rotation = new FRotator(0, (s.Merc.Controller.Rotation.Yaw + 32768) & 0xFFFF, 0);
        Knows(s); // A new visual observation, rather than reading a changed live pose through old memory.
        AiPlan plan = Walk();
        s.Spy.Evasion.Apply(ref plan, s.Spy.World.Now);
        Assert.Equal(SpyEvasionAct.Creep, s.Spy.Evasion.Last);

        // A merc remembered far beyond 2600 uu: no threat.
        AiContact c = s.Spy.Memory.For(s.Merc.Pawn!);
        c.Visible = false;
        c.Position = s.Spy.Pawn!.Location + new FVector(4000f, 0f, 0f);
        plan = Walk();
        s.Spy.Evasion.Apply(ref plan, s.Spy.World.Now);
        Assert.Equal(SpyEvasionAct.None, s.Spy.Evasion.Last);
        Assert.True(plan.Keys.FastSpeed);
    }

    /// <summary>Light data with a black ambient everywhere: the spy is in the dark from every side.</summary>
    private static AiLightField Dark => new([], new System.Collections.Generic.Dictionary<int, AiRgb> { [0] = default }, _ => 0);

    /// <summary>
    /// In the light (TestBox has no light data and reads the average 0.6) a merc sees the spy whatever its speed: the
    /// bot walks at full speed instead of creeping. Playtest: Normal spy bots crept at 88 uu/s across TestBox under
    /// the guards' eyes and no spy reached a terminal in 11 of 12 matches.
    /// </summary>
    [Fact]
    public void ASpyBotInTheLightDoesNotCreep()
    {
        using Scene? s = Start(900f, spy: AiDifficulty.For(AiSkill.Normal));
        Assert.SkipWhen(s is null, "The TestBox fixture is not in this checkout.");
        Knows(s!);
        s!.Merc.Pawn!.Rotation = new FRotator(0, (s.Merc.Controller.Rotation.Yaw + 32768) & 0xFFFF, 0);
        Assert.True(s.Spy.Evasion.Threatened(s.Spy.Pawn!, out _));
        AiPlan plan = Walk();
        s.Spy.Evasion.Apply(ref plan, s.Spy.World.Now);
        Assert.Equal(SpyEvasionAct.None, s.Spy.Evasion.Last);
        Assert.True(plan.Keys.FastSpeed);
    }

    [Fact]
    public void AHardSpyBotDrawsWithTheLongPressNearAMerc()
    {
        using Scene? s = Start(900f, spy: AiDifficulty.For(AiSkill.Hard));
        Assert.SkipWhen(s is null, "The TestBox fixture is not in this checkout.");
        Knows(s!);
        Assert.Equal(0, s!.Spy.Pawn!.WeaponLoaded);
        float now = s.Spy.World.Now;
        var tap = new AiPlan { Keys = new AiInput { bFireMode = 1 } };
        s.Spy.Evasion.Apply(ref tap, now);
        Assert.Equal(1, tap.Keys.bFireMode);
        Assert.Equal(1, s.Spy.Evasion.HoldDraws);

        // The brain lets go after its tap; the press is held to 0.55 s, then released.
        var released = new AiPlan();
        s.Spy.Evasion.Apply(ref released, now + 0.3f);
        Assert.Equal(1, released.Keys.bFireMode);
        released = new AiPlan();
        s.Spy.Evasion.Apply(ref released, now + 0.6f);
        Assert.Equal(0, released.Keys.bFireMode);
    }
}
