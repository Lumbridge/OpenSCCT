using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScctGame.Hud;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-motion</c>: the merc's motion tracking as a player uses it (spec/equipment/motion-tracking.md).
/// SteeE offline as the merc with one spy bot, the listen server on its own thread as the launcher runs it. The bot is
/// held on the server (its keys written by the test, the way its own brain would) about 900 uu in front of the merc.
/// The merc's motion vision goes on with the key bound to <c>SwitchHeadset 1</c> (KeyPilot: a Godot key event through
/// PlayInput and the client). Then, on the merc's own machine (the client): a spy standing still is not locked; a
/// running spy (423.5 uu/s) is locked by the client's goggle tracker, its bracket corners are drawn on the HUD around
/// the spy's projection and the detect sound plays; a creeping spy (crouched, slow: 88.5 uu/s) is not locked once
/// the earlier lock has run out (about 2 s); a running spy that stops keeps its bracket about 2 s and loses it; the
/// key again turns the vision and the tracker off. With a window and SVM_PLAY_SHOTS, each step saves the window.
/// </summary>
public sealed class PlayMotionSmoke : SmokeTest
{
    public const string TestName = "play-motion";

    private const int TeardownFrames = 6;
    private const float SpyDistance = 900f;
    private static readonly Name DetectSound = new("Armes_impacts.Gadgets.Vision_movt_detect");

    /// <summary>What the server hook does with the spy bot (set on the main thread, read on the server thread).</summary>
    private enum SpyMode
    {
        Free,
        Still,
        Run,
        Creep,
    }

    private readonly List<string> _report = new();
    private readonly List<(string Name, double Timeout, Action Begin, Func<string?> Done)> _steps = new();
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private PlayInspector? _inspector;
    private volatile SpyMode _mode = SpyMode.Free;
    private volatile bool _placed;
    private volatile int _yawToSpy;
    private FVector _spot;
    private bool _hooked;
    private int _step = -1;
    private double _stepAt;
    private int _teardown = -1;
    private SmokeResult _final;
    private int _detectSounds;
    private int _locksSeen;
    private float _lockStart = -1f;
    private double _lockGoneAt = -1;

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_teardown >= 0)
        {
            return ++_teardown < TeardownFrames ? SmokeResult.Running : _final;
        }

        if (_root is null)
        {
            // SVM_MOTION_WINDOW=WxH resizes the window first (other aspects: the picture is laid out at most 16:9 and
            // stretched wider, as the aspect smoke checks).
            if (System.Environment.GetEnvironmentVariable("SVM_MOTION_WINDOW") is { } want && DisplayServer.GetName() != "headless")
            {
                string[] wh = want.Split('x');
                if (wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h))
                {
                    context.Tree.Root.Size = new Vector2I(w, h);
                }
            }

            _root = Start(context);
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error)
        {
            return Finish(SmokeResult.Fail("play scene did not load: " + error));
        }

        if (!_root.IsLoaded || _root.Session is not { } session)
        {
            return SmokeResult.Running;
        }

        if (session.ServerFailure is { } failure)
        {
            return Finish(SmokeResult.Fail("server failed: " + failure.Message));
        }

        if (!_hooked)
        {
            _hooked = true;
            session.Host.BeforeWorldTick += _ => DriveSpy(session);
            _inspector = new PlayInspector(_root, InspectChecks.Hud | InspectChecks.Input);
            _inspector.Attach();
        }

        _inspector!.Frame();
        CountSounds(session);
        if (_keys is null)
        {
            SPlayerController? pc = session.LocalController;
            if (pc is null || session.LocalPawn is null || !pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal) ||
                _inspector.Seconds < 3.0)
            {
                return _inspector.Seconds > 90.0 ? Finish(SmokeResult.Fail("the merc never walked")) : SmokeResult.Running;
            }

            _keys = KeyPilot.Attach(_root);
            _mode = SpyMode.Still;
            Build(session);
            return SmokeResult.Running;
        }

        _keys.Update();
        if (!_keys.Ready)
        {
            return SmokeResult.Running;
        }

        if (_step < 0)
        {
            if (!_placed)
            {
                return _inspector.Seconds > 120.0 ? Finish(SmokeResult.Fail("the spy bot was not placed in front of the merc")) : SmokeResult.Running;
            }

            Next(0);
        }

        (string name, double timeout, _, Func<string?> done) = _steps[_step];
        _inspector.Phase = name;
        string? detail = done();
        double elapsed = _inspector.Seconds - _stepAt;
        if (detail is not null && detail.StartsWith("FAIL", StringComparison.Ordinal))
        {
            Shot(name + "-FAILED");
            return Finish(SmokeResult.Fail($"step '{name}': {detail} | {Describe(session)} | done: {string.Join(" || ", _report)}"));
        }

        if (detail is null && elapsed > timeout)
        {
            Shot(name + "-TIMEOUT");
            return Finish(SmokeResult.Fail($"step '{name}' had no result after {timeout:F1} s | {Describe(session)} | done: {string.Join(" || ", _report)}"));
        }

        if (detail is null)
        {
            return SmokeResult.Running;
        }

        Shot(name);
        _report.Add($"{name}: {detail}");
        GD.Print($"SVM_SMOKE {TestName} ok {name}: {detail}");
        if (_step + 1 >= _steps.Count)
        {
            return Finish(SmokeResult.Pass(string.Join(" || ", _report)));
        }

        Next(_step + 1);
        return SmokeResult.Running;
    }

    private static PlayRoot Start(SmokeContext context)
    {
        PlayConfig launcher = PlayConfig.FromOptions(context.App.Options);
        PlayConfig config = launcher with
        {
            Settings = launcher.Settings with
            {
                Map = "SteeE",
                Side = PlaySide.Merc,
                Mode = "Story",
                Rules = PlayRules.PlayLevel,
                HostMode = PlayHostMode.Thread,
                Bots = 1,
                Difficulty = "normal",
            },
            Render = true,
            Hud = true,
            TicksPerFrame = 0,
            Pilot = null,
        };
        PlayRoot root = PlayRoot.Create(config);
        context.Tree.Root.AddChild(root);
        return root;
    }

    private void Next(int index)
    {
        _step = index;
        _stepAt = _inspector!.Seconds;
        _steps[index].Begin();
    }

    private double StepSeconds => _inspector!.Seconds - _stepAt;

    // ---- the steps ------------------------------------------------------------------------------------------------

    private void Build(PlaySession session)
    {
        _steps.Add(("face the spy (mouse)", 6.0, () => { }, () =>
        {
            if (session.LocalController is not { } pc)
            {
                return null;
            }

            int err = (((_yawToSpy - pc.Rotation.Yaw) + 32768) & 0xFFFF) - 32768;
            if (Math.Abs(err) < 600)
            {
                return $"view yaw {pc.Rotation.Yaw & 0xFFFF}, the spy at yaw {_yawToSpy}";
            }

            // A mouse move toward the spy: about 18 yaw units a pixel at the default sensitivity, a few pixels a frame.
            _keys!.Look(Math.Clamp(err / KeyPilot.UnitsPerCount, -40f, 40f), 0f);
            return null;
        }));

        _steps.Add(("motion vision on (key SwitchHeadset 1)", 6.0, () =>
        {
            if (!_keys!.Tap("SwitchHeadset 1"))
            {
                _keys.Tap("SwitchHeadset 3");
            }
        }, () =>
        {
            SPlayerController? pc = session.LocalController;
            SMotionTrackingVision? t = HudMotionTracking.TrackerOf(pc);
            return pc?.RendMap == SGoggle.REN_MotionTrackingVision && t is not null && t.IsActive()
                ? $"RendMap 37, the client's tracker is active (key {_keys!.KeyOf("SwitchHeadset 1")})" : null;
        }));

        _steps.Add(("a spy standing still", 4.0, () =>
        {
            _mode = SpyMode.Still;
            _stillFrom = session.LocalController?.Level.TimeSeconds ?? 0f;
        }, () =>
        {
            if (Tracker(session) is not { } t || ClientSpy(session) is not { } spy)
            {
                return StepSeconds > 3.0 ? "FAIL: no tracker or no spy on the client" : null;
            }

            // A lock from the spy's placement may still be fading at the start; a new one is a failure.
            int i = t.FindLock(spy);
            if (i >= 0 && t.LockedObjects[i].StartTime > _stillFrom + 0.6f)
            {
                return $"FAIL: a still spy was locked (speed {Speed(spy):F1})";
            }

            return StepSeconds >= 3.0 && i < 0 && _root!.Hud?.Motion.DrawnLocks == 0
                ? $"no lock, no bracket (spy speed {Speed(spy):F1} uu/s at {(spy.Location - session.LocalPawn!.Location).Size():F0} uu)" : null;
        }));

        // The spy spawns crouched and its bot taps crouch every 32 ticks until it stands, which can take two seconds of
        // the step before the run reaches full speed.
        _steps.Add(("a spy running across", 5.0, () =>
        {
            _mode = SpyMode.Run;
            _detectSounds = 0;
        }, () =>
        {
            if (Tracker(session) is not { } t || ClientSpy(session) is not { } spy || session.LocalController is not { } pc)
            {
                return null;
            }

            int i = t.FindLock(spy);
            HudMotionTracking? hud = _root!.Hud?.Motion;
            if (i < 0 || hud is null || hud.DrawnLocks == 0 || _detectSounds == 0)
            {
                return null;
            }

            // The corners sit on the spy's box: its projected centre lies inside the four corners' span.
            Vector2? centre = _root.Hud!.Projector?.Invoke(spy.Location);
            List<MagmaQuad> corners = Quads(hud, "Vision_lock");
            if (centre is not { } c || corners.Count < 4)
            {
                return null;
            }

            float minX = corners.Min(q => q.MinX), maxX = corners.Max(q => q.MaxX);
            float minY = corners.Min(q => q.MinY), maxY = corners.Max(q => q.MaxY);
            if (c.X < minX || c.X > maxX || c.Y < minY || c.Y > maxY)
            {
                return StepSeconds > 1.5 ? $"FAIL: the bracket ({minX:F0}..{maxX:F0}, {minY:F0}..{maxY:F0}) is not on the spy ({c.X:F0}, {c.Y:F0})" : null;
            }

            _lockStart = t.LockedObjects[i].StartTime;
            if (pc.Level.TimeSeconds - _lockStart < 0.6f)
            {
                // Keep running until the bracket has closed in (focus 0.12 s, echoes 0.2 s more).
                return null;
            }

            // What the merc sees inside the box: the post pass redraws the scene there in clean monochrome.
            Vector2 screen = _root.Hud!.DrawnScreen;
            Rect2[] clean = _root.View?.Effect.Settings.CleanRects ?? [];
            if (!clean.Any(r => r.HasPoint(c / screen)))
            {
                return StepSeconds > 2.0 ? $"FAIL: no clean view rectangle on the spy in the picture (HUD rects {hud.CleanRects.Count}, post rects {clean.Length})" : null;
            }

            if (DisplayServer.GetName() != "headless" && _root.View!.Effect.LastDrawnCleanRects == 0)
            {
                return StepSeconds > 2.0 ? "FAIL: the post pass drew no clean view rectangle" : null;
            }

            _locksSeen++;
            return $"clean view {clean.Length} rect(s), locked {pc.Level.TimeSeconds - _lockStart:F2} s ago at {Speed(spy):F1} uu/s, {corners.Count} corner quads " +
                   $"({minX:F0}..{maxX:F0} x {minY:F0}..{maxY:F0} on the spy at {c.X:F0}, {c.Y:F0}), detect sounds {_detectSounds}";
        }));

        _steps.Add(("the spy creeps (crouched, slow)", 7.0, () => _mode = SpyMode.Creep, () =>
        {
            if (Tracker(session) is not { } t || ClientSpy(session) is not { } spy)
            {
                return null;
            }

            int i = t.FindLock(spy);
            if (i >= 0 && t.LockedObjects[i].StartTime > _lockStart + 0.01f && StepSeconds > 1.0)
            {
                return $"FAIL: a new lock on a creeping spy (speed {Speed(spy):F1}, crouched {spy.bIsCrouched})";
            }

            if (i >= 0 || StepSeconds < 4.0)
            {
                return null;
            }

            return spy.bIsCrouched && Speed(spy) is > 60f and < 120f && _root!.Hud?.Motion.DrawnLocks == 0
                ? $"the lock ran out and none came back (speed {Speed(spy):F1} uu/s, crouched)" : null;
        }));

        _steps.Add(("the spy runs, then stops", 7.0, () =>
        {
            _mode = SpyMode.Run;
            _lockGoneAt = -1;
        }, () =>
        {
            if (Tracker(session) is not { } t || ClientSpy(session) is not { } spy)
            {
                return null;
            }

            int i = t.FindLock(spy);
            if (_mode == SpyMode.Run && i >= 0 && !t.LockedObjects[i].bRemove && StepSeconds > 0.5)
            {
                _mode = SpyMode.Still;
                _stopAt = _inspector!.Seconds;
                return null;
            }

            if (_mode == SpyMode.Still && _stopAt > 0 && i < 0 && _lockGoneAt < 0)
            {
                _lockGoneAt = _inspector!.Seconds;
            }

            return _lockGoneAt > 0
                ? $"the bracket stayed {_lockGoneAt - _stopAt:F2} s after the stop (hold 1.5 s, fade 0.25 s, next scan)" : null;
        }));

        _steps.Add(("motion vision off (the key again)", 6.0, () => _keys!.Tap("SwitchHeadset 1"), () =>
        {
            SPlayerController? pc = session.LocalController;
            SMotionTrackingVision? t = HudMotionTracking.TrackerOf(pc);
            return pc?.RendMap == SGoggle.REN_DynLight && (t is null || (!t.IsActive() && t.LockedObjects.Count == 0))
                ? "RendMap 5, tracker inactive, no locks" : null;
        }));
    }

    private double _stopAt = -1;
    private float _stillFrom;

    // ---- the server side ------------------------------------------------------------------------------------------

    /// <summary>
    /// On the server thread before each world tick: the spy bot is held and placed in front of the merc once, then
    /// its keys are written after its own (idle) input, as its brain would write them.
    /// </summary>
    private void DriveSpy(PlaySession session)
    {
        if (_mode == SpyMode.Free)
        {
            return;
        }

        if (session.Bots is not { } bots || session.ServerController?.Pawn is not SBasePawn merc)
        {
            Why($"no bots ({session.Bots is null}) or no server merc ({session.ServerController?.Pawn is null})");
            return;
        }

        AiController? bot = bots.Bots.FirstOrDefault(b => b.Pawn is SPawnAttaque { bDeleteMe: false, Health: > 0 });
        if (bot?.Pawn is not SBasePawn spy)
        {
            Why($"no spy bot among {bots.Bots.Count}: {string.Join(", ", bots.Bots.Select(b => b.Pawn?.GetType().Name ?? "none"))}");
            return;
        }

        bot.Held = true;
        if (!_placed)
        {
            // Setup: the spy stays where it stands (a teleported bot's proxy does not follow on the client, see the
            // report); the merc, the local player, is moved to a spot in sight of it with room for the spy to run
            // toward and away along the line between them, as the sandbox captures placed the players.
            if (spy.Physics != EPhysics.PHYS_Walking)
            {
                return;
            }

            FVector start = merc.Location;
            int yaw = -1;
            int sight = 0;
            foreach ((FVector at, int y, int seen) in MercSpots(spy, merc))
            {
                if (merc.SetLocation(at))
                {
                    yaw = y;
                    sight = seen;
                    break;
                }
            }

            if (yaw < 0)
            {
                merc.SetLocation(start);
                Why($"no spot for the merc round the spy at {spy.Location}");
                return;
            }

            _spot = spy.Location;
            _yawToSpy = yaw;
            _placed = true;
            Why($"the merc moved to {merc.Location} facing the spy at {spy.Location}, yaw {yaw}, sees {sight} of 15 points of the spy's run");
        }

        _serverSpyAt = spy.Location;
        var input = new AiInput { View = new FRotator(0, _yawToSpy, 0) };
        switch (_mode)
        {
            case SpyMode.Still:
                input.FastSpeed = true;
                if (spy.bIsCrouched && spy.Physics == EPhysics.PHYS_Walking && (session.TicksRun & 31) == 0)
                {
                    input.bCrouch = 1;
                }

                break;
            case SpyMode.Run:
                input.BT_Up = 1;
                input.FastSpeed = true;
                if (spy.bIsCrouched && (session.TicksRun & 31) == 0)
                {
                    input.bCrouch = 1;
                }

                break;
            case SpyMode.Creep:
                input.BT_Up = 1;
                input.FastSpeed = false;
                if (!spy.bIsCrouched && (session.TicksRun & 31) == 0)
                {
                    input.bCrouch = 1;
                }

                break;
        }

        // Back and forth on the line from the merc: 220 uu each way from the spot, away from the merc and back.
        FVector away = new FRotator(0, _yawToSpy, 0).Vector();
        float along = (spy.Location - _spot) | away;
        if (along > 220f)
        {
            _backwards = true;
        }
        else if (along < -220f)
        {
            _backwards = false;
        }

        // Against something before the end of the run: turn round.
        bool moving = _mode is SpyMode.Run or SpyMode.Creep;
        _stuckTicks = moving && spy.Physics == EPhysics.PHYS_Walking && spy.Velocity.Size() < 20f ? _stuckTicks + 1 : 0;
        if (_stuckTicks > 30)
        {
            _stuckTicks = 0;
            _backwards = !_backwards;
        }

        if (_backwards)
        {
            input.View = new FRotator(0, (_yawToSpy + 32768) & 0xFFFF, 0);
        }

        input.ApplyTo(bot.Controller);
    }

    private bool _backwards;
    private int _stuckTicks;

    private FVector _serverSpyAt;
    private string _lastWhy = "";

    private void Why(string why)
    {
        if (why != _lastWhy)
        {
            _lastWhy = why;
            GD.Print($"SVM_SMOKE {TestName} server: {why}");
        }
    }

    /// <summary>
    /// Spots for the merc 300 to 1500 uu from the spy with a clear line between them at both pawns' heights and a
    /// floor, and 250 uu of room for the spy each way along that line; those that see more of the corridor the spy
    /// runs in first (<see cref="CorridorSight"/>), then the farthest. A spot whose only clear line grazed a wall
    /// corner lost the spy as soon as its run drifted a few units aside, and the tracker (which needs sight) never
    /// locked: that failed the run about 1 time in 5, depending on where the bot had wandered before it was held.
    /// </summary>
    private static IEnumerable<(FVector At, int YawAway, int Sight)> MercSpots(SBasePawn spy, SBasePawn merc)
    {
        FVector up = new(0f, 0f, 40f);
        FVector down = new(0f, 0f, 200f);
        var spots = new List<(FVector At, int YawAway, int Sight)>();
        for (float d = 1500f; d >= 300f; d -= 100f)
        {
            for (int k = 0; k < 32; k++)
            {
                int y = (k * 2048) & 0xFFFF;
                FVector dir = new FRotator(0, y, 0).Vector();
                FVector at = spy.Location + dir * d;
                FVector back = spy.Location - dir * 250f;
                FVector fore = spy.Location + dir * 250f;
                if (Blocked(spy, spy.Location, at) || Blocked(spy, spy.Location + up, at + up) || Blocked(spy, back, fore) ||
                    Blocked(spy, back, at) || !Blocked(spy, back, back - down) || !Blocked(spy, fore, fore - down))
                {
                    continue;
                }

                if (!GadgetWorld.Line(spy, at - new FVector(0f, 0f, 400f), at, ETraceFlags.TRACE_World, out FCheckResult floor))
                {
                    continue;
                }

                FVector mercAt = new(at.X, at.Y, floor.Location.Z + merc.CollisionHeight + 2f);
                spots.Add((mercAt, (y + 32768) & 0xFFFF, CorridorSight(spy, merc, mercAt, dir)));
            }
        }

        // A stable sort: the farthest first among spots that see as much.
        return spots.OrderByDescending(s => s.Sight);
    }

    private static bool Blocked(Actor source, FVector a, FVector b) => GadgetWorld.Line(source, b, a, ETraceFlags.TRACE_World, out _);

    /// <summary>
    /// How many of 15 points of the corridor the standing spy runs in the merc's centre at <paramref name="mercAt"/>
    /// sees: 250 uu each way along <paramref name="dir"/> and 40 uu to either side (a run drifted 37 uu aside in
    /// 200). The creeping spy is crouched, but it must not be locked, so its sight does not count.
    /// </summary>
    private static int CorridorSight(SBasePawn spy, SBasePawn merc, FVector mercAt, FVector dir)
    {
        FVector side = new(-dir.Y, dir.X, 0f);
        float feet = spy.Location.Z - spy.CollisionHeight;
        int seen = 0;
        foreach (float along in (ReadOnlySpan<float>)[-250f, -125f, 0f, 125f, 250f])
        {
            foreach (float aside in (ReadOnlySpan<float>)[-40f, 0f, 40f])
            {
                // The merc's standing half height stands in for the spy's (both centres are 1 uu apart on a floor).
                FVector p = spy.Location + (dir * along) + (side * aside);
                p.Z = feet + merc.CollisionHeight;
                if (!Blocked(spy, mercAt, p))
                {
                    seen++;
                }
            }
        }

        return seen;
    }

    // ---- reading the client ----------------------------------------------------------------------------------------

    private static SMotionTrackingVision? Tracker(PlaySession session) => HudMotionTracking.TrackerOf(session.LocalController);

    private static SBasePawn? ClientSpy(PlaySession session)
    {
        foreach (SPawnAttaque p in session.Client.World.Level.AllActors<SPawnAttaque>())
        {
            if (!p.bDeleteMe && p.Health > 0)
            {
                return p;
            }
        }

        return null;
    }

    private static string ClientSpies(PlaySession session)
    {
        var parts = new List<string>();
        foreach (SPawnAttaque p in session.Client.World.Level.AllActors<SPawnAttaque>())
        {
            parts.Add($"{p} {p.Location} hp {p.Health} {p.Role}");
        }

        return string.Join("; ", parts);
    }

    private static float Speed(Pawn p) => MathF.Sqrt(p.Velocity.X * p.Velocity.X + p.Velocity.Y * p.Velocity.Y);

    private void CountSounds(PlaySession session)
    {
        foreach (ref readonly SimEvent e in session.Client.World.Events.Current)
        {
            if (e.Kind == SimEventKind.PlaySound && e.What == DetectSound)
            {
                _detectSounds++;
            }
        }
    }

    private readonly record struct MagmaQuad(float MinX, float MaxX, float MinY, float MaxY);

    private static List<MagmaQuad> Quads(HudMotionTracking hud, string texture)
    {
        var quads = new List<MagmaQuad>();
        foreach (Hud.Magma.MagmaDrawItem item in hud.DrawList.Items)
        {
            if (item.Texture == texture && item.Points.Length == 4 && item.Colors.Length > 0 && item.Colors[0].A > 0.4f)
            {
                quads.Add(new MagmaQuad(item.Points.Min(p => p.X), item.Points.Max(p => p.X), item.Points.Min(p => p.Y), item.Points.Max(p => p.Y)));
            }
        }

        return quads;
    }

    private string Describe(PlaySession session)
    {
        SPlayerController? pc = session.LocalController;
        SBasePawn? spy = ClientSpy(session);
        SMotionTrackingVision? t = Tracker(session);
        return string.Create(CultureInfo.InvariantCulture,
            $"client spies [{ClientSpies(session)}] server spy {_serverSpyAt}, merc {session.LocalPawn?.Location}, RendMap {pc?.RendMap}, tracker {(t is null ? "none" : t.IsActive() ? "active" : "idle")} locks {t?.LockedObjects.Count}, " +
            $"spy {(spy is null ? "none" : $"{spy.Location} speed {Speed(spy):F1} crouched {spy.bIsCrouched}")}, mode {_mode}, drawn {_root?.Hud?.Motion.DrawnLocks}, sounds {_detectSounds}, " +
            $"scan [{ScanCheck(session, t, spy)}]");
    }

    /// <summary>What the client tracker's scan rules say about the spy now: distance, speed, candidate, hidden, sight.</summary>
    private static string ScanCheck(PlaySession session, SMotionTrackingVision? t, SBasePawn? spy)
    {
        if (t is null || spy is null || session.LocalController is not { } pc || pc.Pawn is not { } viewer)
        {
            return "none";
        }

        FVector v = SMotionTrackingVision.ViewerPoint(pc, viewer);
        return string.Create(CultureInfo.InvariantCulture,
            $"dist {(spy.Location - v).Size():F0} of {t.MaxDetectedDistance + spy.CollisionRadius:F0}, speed {spy.Velocity.Size():F1} min {t.MinDetectedVelocity:F0}, candidate {SMotionTrackingVision.IsCandidate(spy, viewer)}, hidden {spy.bHidden}, deleted {spy.bDeleteMe}, sight {t.InSight(v, spy)}, state {t.GetStateName()}, time {t.Level?.TimeSeconds:F2}");
    }

    /// <summary>With a window and SVM_PLAY_SHOTS naming a folder (keep it under local/), saves the window.</summary>
    private void Shot(string label)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(folder);
        Image image = _root.GetViewport().GetTexture().GetImage();
        string safe = new(label.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        image.SavePng(System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{TestName}-{_inspector?.Seconds ?? 0:000.0}-{safe}.png")));
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _final = result;
        _teardown = 0;
        _keys?.ReleaseAll();
        _root?.QueueFree();
        return SmokeResult.Running;
    }
}
