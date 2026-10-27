using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Combat;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

/// <summary>
/// The merc's motion tracker as decoded (spec/equipment/motion-tracking.md): GetActors (the Reloaded binding), the
/// screen box of the native Tick and the HUD of DrawHUD (UCanvas::RenderMotionTracking). Numbers marked "capture"
/// come from the real game recorded in the sandbox (local/motion-capture, Play Level on SteeE): the spy's speeds per
/// gait, which of them a tracker locked, the lock timings and the steady box of a spy 800 uu away.
/// </summary>
public sealed class MotionTrackingTests
{
    private static (CombatScene Scene, SPlayerController PC, SMotionTrackingVision Tracker) Tracker()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, _) = GadgetKit.Local(scene, false, new FVector(0f, 0f, 100f));
        SMotionTrackingVision tracker = scene.Level.SpawnActor(new SMotionTrackingVision(), default, default)!;
        tracker.PlayerOwner = pc;
        tracker.GotoState(GadgetStates.s_Active);
        return (scene, pc, tracker);
    }

    /// <summary>Runs <paramref name="seconds"/>, holding each mover's velocity (the movement code would set it each tick).</summary>
    private static void Run(CombatScene scene, float seconds, params (Actor Who, FVector Velocity)[] movers)
    {
        for (int i = 0; i < (int)(seconds * Sim.Hz + 0.5f); i++)
        {
            foreach ((Actor who, FVector v) in movers)
            {
                who.Velocity = v;
            }

            scene.World.Step();
        }
    }

    // ---- GetActors -------------------------------------------------------------------------------------------------

    /// <summary>The spy's steady speeds (uu/s) per gait in the real game, and whether a merc's tracker 1800 uu away locked it (capture).</summary>
    public static IEnumerable<object[]> Gaits() =>
    [
        ["unarmed standing, fast", 423.5f, true],
        ["unarmed standing, slow", 169.4f, true],
        ["unarmed crouched, fast", 221.4f, true],
        ["unarmed crouched, slow", 88.5f, false],
        ["gun drawn (tap or hold) standing forward, fast", 150.2f, true],
        ["gun drawn standing strafe, fast", 138.6f, false],
        ["gun drawn crouched forward, fast", 104f, false],
        ["gun drawn standing forward, slow", 87.5f, false],
        ["gun drawn crouched forward, slow", 61.5f, false],
    ];

    [Theory]
    [MemberData(nameof(Gaits))]
    public void TheTrackerLocksTheGaitsTheRealGameLocks(string gait, float speed, bool locked)
    {
        (CombatScene scene, _, SMotionTrackingVision tracker) = Tracker();
        (_, SBasePawn spy) = scene.Player(true, new FVector(1800f, 0f, 100f));
        Run(scene, 1.2f, (spy, new FVector(0f, speed, 0f)));
        Assert.True(locked == tracker.FindLock(spy) >= 0, $"{gait} at {speed} uu/s: locked {!locked}, the real game {locked}");
    }

    [Fact]
    public void TheDrawModeChangesNothingForTheTracker()
    {
        // Capture: a tap draw and a hold draw both give WeaponLoaded 2 and the same speeds (150.2 forward, 138.6
        // strafe); the tracker reads only class, glass, distance, speed and the sight line.
        (CombatScene scene, _, SMotionTrackingVision tracker) = Tracker();
        (_, SBasePawn tap) = scene.Player(true, new FVector(1500f, 0f, 100f));
        (_, SBasePawn hold) = scene.Player(true, new FVector(0f, 1500f, 100f));
        tap.WeaponLoaded = 2;
        hold.WeaponLoaded = 2;
        Run(scene, 1.2f, (tap, new FVector(0f, 150.2f, 0f)), (hold, new FVector(150.2f, 0f, 0f)));
        Assert.True(tracker.FindLock(tap) >= 0 && tracker.FindLock(hold) >= 0);
    }

    [Fact]
    public void ANewLockGoesFirstAndPlaysTheDetectSoundAtTheViewer()
    {
        (CombatScene scene, SPlayerController pc, SMotionTrackingVision tracker) = Tracker();
        (_, SBasePawn first) = scene.Player(true, new FVector(1000f, 0f, 100f));
        Run(scene, 0.6f, (first, new FVector(0f, 300f, 0f)));
        (_, SBasePawn second) = scene.Player(true, new FVector(0f, 1000f, 100f));
        var sounds = new List<SimEvent>();
        for (int i = 0; i < (int)(0.6f * Sim.Hz); i++)
        {
            first.Velocity = new FVector(0f, 300f, 0f);
            second.Velocity = new FVector(300f, 0f, 0f);
            scene.World.Step();
            foreach (ref readonly SimEvent e in scene.World.Events.Current)
            {
                if (e.Kind == SimEventKind.PlaySound && e.What.ToString() == "Armes_impacts.Gadgets.Vision_movt_detect")
                {
                    sounds.Add(e);
                }
            }
        }

        Assert.Equal(2, tracker.LockedObjects.Count);
        Assert.Same(second, tracker.LockedObjects[0].Actor);
        Assert.Same(first, tracker.LockedObjects[1].Actor);
        SimEvent sound = Assert.Single(sounds);
        Assert.Equal(pc.Pawn!.ActorId, sound.ActorId);
        Assert.Equal(pc.Pawn.Location, sound.Location);
        Assert.Equal(SMotionTrackingVisionK.DetectSoundRadius, sound.Radius);
        Assert.True(sound.NoOverride);
    }

    [Fact]
    public void AStaleLockHoldsOneAndAHalfSecondsAndGoesAtTheFirstScanPastItsFade()
    {
        (CombatScene scene, _, SMotionTrackingVision tracker) = Tracker();
        (_, SBasePawn spy) = scene.Player(true, new FVector(1500f, 0f, 100f));
        Run(scene, 0.6f, (spy, new FVector(0f, 300f, 0f)));
        FLockedObject l = Assert.Single(tracker.LockedObjects);
        float start = l.StartTime;

        // Stopped: the next scan marks it stale with EndTime = that scan + 1.5.
        float scan = 0f;
        for (int i = 0; i < Sim.Hz && tracker.LockedObjects[0].EndTime == 0f; i++)
        {
            spy.Velocity = default;
            scene.World.Step();
            scan = scene.Time;
        }

        l = tracker.LockedObjects[0];
        Assert.True(l.bRemove);
        Assert.Equal(scan + 1.5f, l.EndTime, 3);
        Assert.Equal(start, l.StartTime);

        // Capture: the lock disappears 0.5 s after EndTime (the scan after EndTime + OutTime).
        float gone = -1f;
        for (int i = 0; i < 4 * Sim.Hz && gone < 0f; i++)
        {
            scene.World.Step();
            if (tracker.LockedObjects.Count == 0)
            {
                gone = scene.Time;
            }
        }

        Assert.InRange(gone - l.EndTime, 0.25f, 0.5f + 1.5f / Sim.Hz);
    }

    [Fact]
    public void ALockSeenAgainKeepsItsEndTimeAndGoesAtOnceWhenStaleAgain()
    {
        (CombatScene scene, _, SMotionTrackingVision tracker) = Tracker();
        (_, SBasePawn spy) = scene.Player(true, new FVector(1500f, 0f, 100f));
        Run(scene, 0.6f, (spy, new FVector(0f, 300f, 0f)));
        Run(scene, 0.6f, (spy, default));
        float end = tracker.LockedObjects[0].EndTime;
        Assert.True(end > 0f);
        Run(scene, 2f, (spy, new FVector(0f, 300f, 0f)));
        FLockedObject again = Assert.Single(tracker.LockedObjects);
        Assert.False(again.bRemove);
        Assert.Equal(end, again.EndTime);

        // Past its old EndTime + OutTime, the first stale scan removes it.
        Run(scene, 0.6f, (spy, default));
        Assert.Empty(tracker.LockedObjects);
    }

    [Fact]
    public void OnlyAFullyCloakedSpyIsSkipped()
    {
        (CombatScene scene, _, SMotionTrackingVision tracker) = Tracker();
        (_, SBasePawn cloaked) = scene.Player(true, new FVector(1500f, 0f, 100f));
        (_, SBasePawn fading) = scene.Player(true, new FVector(0f, 1500f, 100f));
        cloaked.DesiredGlassEffect = 255;
        fading.DesiredGlassEffect = 128;
        Run(scene, 1.2f, (cloaked, new FVector(0f, 221.4f, 0f)), (fading, new FVector(221.4f, 0f, 0f)));
        Assert.Equal(-1, tracker.FindLock(cloaked));
        Assert.True(tracker.FindLock(fading) >= 0);

        // A merc is never cloak-skipped (the rule names the spy pawn class).
        Assert.True(SMotionTrackingVision.IsCandidate(new SPawnDefense_1Mesh { DesiredGlassEffect = 255 }, new SPawnDefense_1Mesh()));
    }

    [Fact]
    public void ThePastLimitReachAddsTheTargetsRadius()
    {
        (CombatScene scene, _, SMotionTrackingVision tracker) = Tracker();
        float r = new SPawnAttaque_1Mesh().CollisionRadius;
        (_, SBasePawn inside) = scene.Player(true, new FVector(2600f + r - 2f, 0f, 100f));
        (_, SBasePawn outside) = scene.Player(true, new FVector(0f, 2600f + r + 2f, 100f));
        Run(scene, 1.2f, (inside, new FVector(0f, 400f, 0f)), (outside, new FVector(400f, 0f, 0f)));
        Assert.True(tracker.FindLock(inside) >= 0);
        Assert.Equal(-1, tracker.FindLock(outside));
    }

    [Fact]
    public void TheViewerIsTheUsedCameraInTheCameraNetworkAndTheControllerWhileSpectating()
    {
        CombatScene scene = GadgetKit.Scene();
        (SPlayerController pc, SBasePawn merc) = scene.Player(false, new FVector(0f, 0f, 100f));
        SStickyCam cam = scene.Level.SpawnActor(new SStickyCam(), new FVector(500f, 500f, 300f), default)!;
        Assert.Equal(merc.Location, SMotionTrackingVision.ViewerPoint(pc, merc));
        merc.UsedObject = cam;
        Assert.Equal(merc.Location, SMotionTrackingVision.ViewerPoint(pc, merc));
        pc.GotoState(GadgetStates.s_UseObject_CamNetwork);
        Assert.Equal(cam.Location, SMotionTrackingVision.ViewerPoint(pc, merc));
    }

    // ---- the screen box (Tick) -------------------------------------------------------------------------------------

    /// <summary>A pinhole camera at the origin looking along +X, 90 degrees across a 640 x 480 frame (4:3).</summary>
    private static FVector Pinhole(FVector p)
    {
        if (p.X <= 1f)
        {
            return new FVector(0f, 0f, 2f);
        }

        return new FVector(320f + p.Y / p.X * 320f, 240f - p.Z / p.X * 320f, 0.5f);
    }

    [Fact]
    public void TheBoxOfAFarSpyIsWidenedToATenthOfTheFrame()
    {
        // Capture: a spy 800 uu ahead gave a steady box 64 x 64 (x 200.5 to 264.5, y 208.5 to 272.4): the natural box
        // (2 x 35 uu wide) is under MinWidth (0.1 x 640) and is widened about its centre.
        (CombatScene scene, _, SMotionTrackingVision tracker) = Tracker();
        (_, SBasePawn spy) = scene.Player(true, new FVector(800f, 0f, 0f));
        tracker.LockedObjects.Add(new FLockedObject { Actor = spy, StartTime = scene.Time - 1f });
        var rects = new List<MotionHudRect>();
        tracker.ProjectLocks(Pinhole, default, 640f, 480f, scene.Time, CleanRects: rects);
        FLockedObject l = tracker.LockedObjects[0];
        Assert.Equal(64f, l.ScreenPosition2.X - l.ScreenPosition1.X, 3);
        Assert.Equal(320f, (l.ScreenPosition1.X + l.ScreenPosition2.X) * 0.5f, 3);
        float naturalHeight = 2f * spy.CollisionHeight / 800f * 320f;
        Assert.Equal(naturalHeight, l.ScreenPosition2.Y - l.ScreenPosition1.Y, 2);
        MotionHudRect rect = Assert.Single(rects);
        Assert.Equal(l.ScreenPosition1.X, rect.X1);
    }

    [Fact]
    public void TheBoxOfANearPawnIsKeptUnderItsMaximumAndABoxBehindIsNotDrawn()
    {
        (CombatScene scene, _, SMotionTrackingVision tracker) = Tracker();
        (_, SBasePawn near) = scene.Player(true, new FVector(60f, 0f, 0f));
        (_, SBasePawn behind) = scene.Player(true, new FVector(-500f, 0f, 0f));
        tracker.LockedObjects.Add(new FLockedObject { Actor = near, StartTime = scene.Time - 1f });
        tracker.LockedObjects.Add(new FLockedObject { Actor = behind, StartTime = scene.Time - 1f });
        tracker.ProjectLocks(Pinhole, default, 640f, 480f, scene.Time);
        FLockedObject n = tracker.LockedObjects[0];
        Assert.Equal(0.45f * 640f, n.ScreenPosition2.X - n.ScreenPosition1.X, 3);
        Assert.Equal(0.6f * 480f, n.ScreenPosition2.Y - n.ScreenPosition1.Y, 3);
        var hud = new MotionHudDrawList();
        tracker.LockedObjects.RemoveAt(0);
        tracker.BuildHud(hud, new MotionHudInputs { Now = scene.Time });
        Assert.Empty(hud.Tiles);
    }

    // ---- DrawHUD ---------------------------------------------------------------------------------------------------

    private static (SMotionTrackingVision Tracker, SBasePawn Spy) Locked(float start, bool stale = false, float end = 0f, bool friend = false)
    {
        CombatScene scene = GadgetKit.Scene();
        SMotionTrackingVision tracker = scene.Level.SpawnActor(new SMotionTrackingVision(), default, default)!;
        (_, SBasePawn spy) = scene.Player(true, new FVector(800f, 0f, 0f));
        tracker.LockedObjects.Add(new FLockedObject
        {
            Actor = spy,
            StartTime = start,
            EndTime = end,
            bRemove = stale,
            ItsMyFriend = friend,
            ScreenPosition1 = new FVector(200f, 200f, 0.9975f),
            ScreenPosition2 = new FVector(264f, 264f, 0.9976f),
        });
        return (tracker, spy);
    }

    private static List<MotionHudTile> Corners(MotionHudDrawList hud) => hud.Tiles.Where(t => t.Texture == MotionHudTexture.Lock).ToList();

    [Fact]
    public void ANewLockZoomsInFromTheWholeFrameWithCornersFourTimesTheirSize()
    {
        (SMotionTrackingVision tracker, _) = Locked(10f);
        var hud = new MotionHudDrawList();
        tracker.BuildHud(hud, new MotionHudInputs { Now = 10f });
        List<MotionHudTile> c = Corners(hud).Take(4).ToList();
        (float X, float Y, float XL, float YL)[] want = [(0f, 0f, 128f, 128f), (640f, 0f, -128f, 128f), (0f, 480f, 128f, -128f), (640f, 480f, -128f, -128f)];
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(want[i].X, c[i].X, 2);
            Assert.Equal(want[i].Y, c[i].Y, 2);
            Assert.Equal(want[i].XL, c[i].XL, 2);
            Assert.Equal(want[i].YL, c[i].YL, 2);
        }
        Assert.All(c, t => Assert.Equal(SMotionTrackingVisionK.FocusAlpha, t.Colour.A));
        Assert.All(c, t => Assert.Equal((byte)0x80, t.Colour.R));

        // Half way through the focus (0.06 s): half way in, corners 2.5 times.
        hud.Clear();
        tracker.BuildHud(hud, new MotionHudInputs { Now = 10.06f });
        MotionHudTile tl = Corners(hud)[0];
        Assert.Equal(100f, tl.X, 2);
        Assert.Equal(80f, tl.XL, 2);
        MotionHudTile br = Corners(hud)[3];
        Assert.Equal(264f + (640f - 264f) * 0.5f, br.X, 2);
    }

    [Fact]
    public void AHeldLockShowsCornersTheSweepAndInvisibleLabels()
    {
        (SMotionTrackingVision tracker, SBasePawn spy) = Locked(10f);
        spy.Velocity = new FVector(0f, 300f, 0f);
        var hud = new MotionHudDrawList();
        tracker.BuildHud(hud, new MotionHudInputs { Now = 10.12f + 0.25f, ViewerLocation = default });
        List<MotionHudTile> c = Corners(hud);
        Assert.Equal(4, c.Count);
        Assert.Equal((200f, 200f, 32f, 32f), (c[0].X, c[0].Y, c[0].XL, c[0].YL));
        Assert.Equal((264f, 264f, -32f, -32f), (c[3].X, c[3].Y, c[3].XL, c[3].YL));
        Assert.All(c, t => Assert.Equal(SMotionTrackingVisionK.HeldAlpha, t.Colour.A));

        // The sweep: a quarter second after focus, a quarter of the way from the middle (232) to 8 under the top.
        List<MotionHudTile> sweep = hud.Tiles.Where(t => t.Texture == MotionHudTexture.Sweep).ToList();
        Assert.Equal(2, sweep.Count);
        Assert.Equal(232f - 24f * 0.25f, sweep[0].Y, 2);
        Assert.Equal((204f, 56f, 16f), (sweep[0].X, sweep[0].XL, sweep[0].YL));
        Assert.Equal(-16f, sweep[1].YL);
        Assert.Equal((byte)(51 - 12.5f), sweep[0].Colour.A);

        // The labels: speed and distance in metres, alpha 0 (they do not show on an enemy).
        Assert.Equal(new[] { "3.00 m/s", "8.00 m" }, hud.Texts.Select(t => t.Text).ToArray());
        Assert.All(hud.Texts, t => Assert.Equal(0, t.Colour.A));
        Assert.Equal(274f, hud.Texts[1].Y);
    }

    [Fact]
    public void AFadingLockOpensBackToTheFrameWithFadingCornersAndEchoes()
    {
        (SMotionTrackingVision tracker, _) = Locked(10f, stale: true, end: 12f);
        var hud = new MotionHudDrawList();
        tracker.BuildHud(hud, new MotionHudInputs { Now = 12.125f });
        List<MotionHudTile> c = Corners(hud);
        // Half the fade: corners 2.5 times, alpha trunc(31.5 + 1), the box half way to the frame.
        Assert.Equal(80f, c[0].XL, 3);
        Assert.Equal((byte)32, c[0].Colour.A);
        Assert.Equal(100f, c[0].X, 3);
        Assert.DoesNotContain(hud.Tiles, t => t.Texture == MotionHudTexture.Sweep);

        // Echoes 0.05 s apart, drawn only while outside the box: the first two here (their top left corner at
        // 0.7 and 0.9 of the box's, the next two would be inside). Alpha trunc(remaining x 128 / i / OutTime + 1).
        Assert.Equal(4 + 2 * 4, c.Count);
        Assert.Equal(140f, c[4].X, 2);
        Assert.Equal((byte)65, c[4].Colour.A);
        Assert.Equal((byte)33, c[8].Colour.A);
    }

    [Fact]
    public void TheCornersTurnGreyForAMomentAfterTheMercsShotHitsAPawn()
    {
        (SMotionTrackingVision tracker, _) = Locked(10f);
        var hud = new MotionHudDrawList();
        tracker.BuildHud(hud, new MotionHudInputs { Now = 11f, LastShotPawn = 10.9f });
        MotionHudColour grey = Corners(hud)[0].Colour;
        Assert.Equal((byte)128, grey.R);
        Assert.Equal((byte)128, grey.G);
        Assert.Equal((byte)128, grey.B);
        hud.Clear();
        tracker.BuildHud(hud, new MotionHudInputs { Now = 11f, LastShotPawn = 10.75f });
        MotionHudColour back = Corners(hud)[0].Colour;
        Assert.Equal((byte)128, back.R);
        Assert.Equal((byte)(0x5b * 0.25f + 96f), back.G);
        Assert.Equal((byte)104, back.B);
    }

    [Fact]
    public void AFriendsLockBlinksHoldFireWithTheFriendMarks()
    {
        (SMotionTrackingVision tracker, _) = Locked(10f, friend: true);
        var hud = new MotionHudDrawList();
        tracker.BuildHud(hud, new MotionHudInputs { Now = 11.6f });
        MotionHudText hold = Assert.Single(hud.Texts, t => t.Text == "Hold Fire");
        Assert.Equal((byte)200, hold.Colour.R);
        Assert.Equal((byte)0xff, hold.Colour.A);
        Assert.Equal(2, hud.Tiles.Count(t => t.Texture == MotionHudTexture.Friend));
        hud.Clear();
        tracker.BuildHud(hud, new MotionHudInputs { Now = 12.1f });
        Assert.DoesNotContain(hud.Texts, t => t.Text == "Hold Fire");
    }
}
