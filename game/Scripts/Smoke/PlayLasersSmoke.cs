using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-lasers</c>: where the lasers leave the drawn guns, played from the keys as the launchers run the
/// game (SteeE, Play Level rules, the listen server on its own thread, wall clock, HUD). Every press is a Godot input
/// event sent by <see cref="KeyPilot"/> for the key the layout binds, read by PlayInput and sent over loopback.
/// <para>
/// Spy leg (one merc bot): a tap of E draws the pistol with its laser; with the view level, up, down, crouched and
/// walking, the drawn beam must start on the drawn pistol where the original starts it (the hand's frame plus
/// 30 X - 4 Y + 8 Z, spy-shocker.md rule 14.4: a few uu behind the pistol's FX bone) and run to the dot. Then the
/// server turns the merc bot's laser on (the only test hook, on the server's thread: it also holds the bot in view and
/// keeps the spy alive) and the bot's beam, drawn on the spy's screen, must start at the FX bone of the rifle drawn in
/// the bot's hands (merc-laser.md, NativeReplaceLaserDistant).
/// </para>
/// <para>
/// Merc leg (one spy bot): Q turns the laser on; the local merc's beam must start at his first-person rifle's FX bone
/// pulled back 50 uu (rule 9), level, up and down.
/// </para>
/// With a window and SVM_PLAY_SHOTS naming a folder (under local/) the window is saved at each check.
/// </summary>
public sealed class PlayLasersSmoke : SmokeTest
{
    public const string TestName = "play-lasers";

    private const int TeardownFrames = 6;

    /// <summary>The drawn beam may start this far from where the drawn gun puts it (uu).</summary>
    private const float StartTolerance = 1f;

    /// <summary>Frames a placement check samples before it passes.</summary>
    private const int CheckFrames = 20;

    private static readonly Name BoneFx = new("FX");

    private readonly List<string> _done = new();
    private readonly List<StepDef> _steps = new();
    private readonly ServerHook _server = new();
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private SmokeContext? _context;
    private int _index;
    private int _frame;
    private long _stepTick;
    private long _lastActTick = -1;
    private int _teardown = -1;
    private SmokeResult _final;
    private int _shots;
    private bool _hooked;
    private string? _failed;
    private Stats _stats = new();

    private sealed record StepDef(string Name, PlaySide Side, double TimeoutSeconds, Action<PlayLasersSmoke> Act, Func<PlayLasersSmoke, string?> Done);

    public override string Name => TestName;

    public PlayLasersSmoke()
    {
        Add(PlaySide.Spy, "settle", 60, _ => { }, s => s.Walking && s.Ticks > 90 && s._keys!.Ready ? $"in {s.PC!.GetStateName()}" : null);
        Add(PlaySide.Spy, "night vision (F1)", 4, s => s.TapAt("IK_F1", 2), s => s.PC is { RendMap: not 0 } && s.Ticks > 10 ? "on" : null);
        Add(PlaySide.Spy, "tap E: draw with the laser", 8, s => s.TapAt("FireMode", 2), s => s.Pistol is { bLaserOn: true } && s.Aiming && s.Ticks > 30 ? "laser on" : null);
        // The spy starts crouched on SteeE.
        Add(PlaySide.Spy, "spy beam, crouched, view level", 6, s => s.AimPitch(0), s => s.CheckSpyBeam(0, crouched: true));
        Add(PlaySide.Spy, "spy beam, crouched, aiming up", 6, s => s.AimPitch(2500), s => s.CheckSpyBeam(2500, crouched: true));
        Add(PlaySide.Spy, "spy beam, crouched, aiming down", 6, s => s.AimPitch(-3500), s => s.CheckSpyBeam(-3500, crouched: true));
        Add(PlaySide.Spy, "stand (C)", 4, s => s.TapAt("Crouch", 2), s => s.Pawn is { bIsCrouched: false } && s.Ticks > 40 ? "standing" : null);
        Add(PlaySide.Spy, "spy beam, standing, view level", 6, s => s.AimPitch(0), s => s.CheckSpyBeam(0, crouched: false));
        Add(PlaySide.Spy, "spy beam, standing, aiming up", 6, s => s.AimPitch(2500), s => s.CheckSpyBeam(2500, crouched: false));
        Add(PlaySide.Spy, "spy beam, standing, aiming down", 6, s => s.AimPitch(-3500), s => s.CheckSpyBeam(-3500, crouched: false));
        Add(PlaySide.Spy, "spy beam, walking", 6, s => s.Walk(), s => s.CheckSpyBeam(null, crouched: false, moving: true));
        Add(PlaySide.Spy, "crouch (C) while walking", 4, s => s.Walk(tapCrouch: true), s => s.Pawn is { bIsCrouched: true } && s.Ticks > 40 ? "crouched" : null);
        Add(PlaySide.Spy, "spy beam, crouched, walking", 6, s => s.Walk(), s => s.CheckSpyBeam(null, crouched: true, moving: true));
        Add(PlaySide.Spy, "merc bot's beam on the spy's screen", 25, s => s.BotLaser(), s => s.CheckBotBeam());
        Add(PlaySide.Merc, "settle", 60, _ => { }, s => s.Walking && s.Ticks > 90 && s._keys!.Ready && s.Pawn?.MyWeapon is not null ? $"in {s.PC!.GetStateName()}" : null);
        Add(PlaySide.Merc, "laser on (Q)", 6, s => s.TapAt("GadLaserSwitch", 2), s => s.MercLaserOn && s.Ticks > 30 ? "laser on" : null);
        Add(PlaySide.Merc, "merc beam, view level", 6, s => s.AimPitch(0), s => s.CheckMercBeam(0));
        Add(PlaySide.Merc, "merc beam, aiming up", 6, s => s.AimPitch(2500), s => s.CheckMercBeam(2500));
        Add(PlaySide.Merc, "merc beam, aiming down", 6, s => s.AimPitch(-3500), s => s.CheckMercBeam(-3500));
        // grenade-throw.md open question, seen in the sandbox (local/lasers/capture.py --side wall): the original's merc
        // backed into the wall east of the SteeE yard lane (35 uu behind his eye) fires his rifle and throws a frag.
        Add(PlaySide.Merc, "back into the wall (S)", 10, s => s.BackToWall(), s => s.AtWall());
        Add(PlaySide.Merc, "rifle shot with the wall behind (left mouse)", 4, s => s.Shoot(), s => s.Shot());
        Add(PlaySide.Merc, "frag with the wall behind (right mouse)", 6, s => s.Throw(), s => s.Thrown());
    }

    private void Add(PlaySide side, string name, double timeout, Action<PlayLasersSmoke> act, Func<PlayLasersSmoke, string?> done) =>
        _steps.Add(new StepDef(name, side, timeout, act, done));

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        if (_teardown >= 0)
        {
            return ++_teardown < TeardownFrames ? SmokeResult.Running : _final;
        }

        StepDef step = _steps[_index];
        if (_root is null)
        {
            _root = StartPlay(context, step.Side);
            _hooked = false;
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error)
        {
            return Finish(SmokeResult.Fail("play scene did not load: " + error));
        }

        if (!_root.IsLoaded || _root.Session is not { } session || session.LocalPawn is null)
        {
            return SmokeResult.Running;
        }

        if (session.ServerFailure is { } failure)
        {
            return Finish(SmokeResult.Fail("server failed: " + failure.Message));
        }

        if (!_hooked)
        {
            if (session.Host is not { } host)
            {
                return Finish(SmokeResult.Fail("no listen server in this process"));
            }

            _server.Reset();
            host.BeforeWorldTick += _server.OnServerTick;
            _keys = KeyPilot.Attach(_root);
            _hooked = true;
            _stepTick = session.TicksRun;
        }

        _keys!.Update();
        string? detail = _frame == 0 ? null : step.Done(this);
        if (_failed is not null)
        {
            Shot(step.Name + " FAILED");
            return Finish(SmokeResult.Fail($"[{step.Side}] step '{step.Name}': {_failed} | {Describe()} | done: {string.Join(" | ", _done)}"));
        }

        if (detail is not null)
        {
            string line = $"[{step.Side}] {step.Name}: {detail}";
            _done.Add(line);
            GD.Print($"SVM_SMOKE {TestName} ok {line} ({Ticks} ticks)");
            Shot(step.Name);
            _keys.ReleaseAll();
            _index++;
            _frame = 0;
            _stats = new Stats();
            _stepTick = session.TicksRun;
            if (_index >= _steps.Count)
            {
                return Finish(SmokeResult.Pass(string.Join(" | ", _done)));
            }

            if (_steps[_index].Side != step.Side)
            {
                Unhook();
                _root.QueueFree();
                _root = null;
            }

            return SmokeResult.Running;
        }

        if (Ticks > step.TimeoutSeconds * Sim.Hz)
        {
            Shot(step.Name + " FAILED");
            return Finish(SmokeResult.Fail($"[{step.Side}] step '{step.Name}' had no result after {step.TimeoutSeconds} s: {Describe()} | done: {string.Join(" | ", _done)}"));
        }

        // One step action a simulation tick (the aim steers from the last tick's rotation).
        if (session.TicksRun == _lastActTick)
        {
            return SmokeResult.Running;
        }

        _lastActTick = session.TicksRun;
        step.Act(this);
        _frame++;
        return SmokeResult.Running;
    }

    private static PlayRoot StartPlay(SmokeContext context, PlaySide side)
    {
        PlayConfig launcher = PlayConfig.FromOptions(context.App.Options);
        PlayConfig config = launcher with
        {
            Settings = launcher.Settings with
            {
                Map = "SteeE",
                Side = side,
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

    private void Unhook()
    {
        _keys?.ReleaseAll();
        if (_root?.Session?.Host is { } host)
        {
            host.BeforeWorldTick -= _server.OnServerTick;
        }
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _final = result;
        _teardown = 0;
        Unhook();
        _root?.QueueFree();
        _root = null;
        return SmokeResult.Running;
    }

    // ---- what the steps see -------------------------------------------------------------------------------------

    private PlaySession Session => _root!.Session!;

    private int Ticks => (int)(Session.TicksRun - _stepTick);

    private SPlayerController? PC => Session.LocalController;

    private SBasePawn? Pawn => Session.LocalPawn;

    private SWeaponAttaque? Pistol => Pawn?.MyWeapon as SWeaponAttaque;

    private bool Walking => PC?.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal) == true;

    private bool Aiming => PC is { } pc && (pc.IsInState("s_PlayerWalkingSt2H") || pc.IsInState("s_PlayerWalkingCr2H"));

    private bool MercLaserOn => Pawn is SPAWNDEFENSE { bLaserOn: true } && _root!.MercLasers is { LocalVisible: true };

    private string? Fail(string why)
    {
        _failed = why;
        return null;
    }

    private void TapAt(string action, int frame)
    {
        if (_frame == frame && !_keys!.Tap(action))
        {
            Fail($"no key is bound to {action}");
        }
    }

    /// <summary>Turns the view to <paramref name="pitch"/> with the mouse (the yaw kept).</summary>
    private void AimPitch(int pitch)
    {
        if (PC is not { } pc)
        {
            return;
        }

        int e = (short)(pitch - pc.Rotation.Pitch);
        if (Math.Abs(e) <= 80)
        {
            return;
        }

        float width = _root!.GetViewport().GetVisibleRect().Size.X;
        float k = width > 1f && width < 640f ? KeyPilot.ReferenceWidth / width : 1f;
        _keys!.Look(0f, Math.Clamp(-e / KeyPilot.UnitsPerCount, -20f, 20f) * k);
        Input.FlushBufferedEvents();
    }

    private void Walk(bool tapCrouch = false)
    {
        AimPitch(0);
        if (_frame == 2)
        {
            _keys!.Press("BT_Up");
            if (tapCrouch)
            {
                _keys.Tap("Crouch");
            }
        }
    }

    private bool PitchNear(int? pitch) => pitch is null || (PC is { } pc && Math.Abs((short)(pitch.Value - pc.Rotation.Pitch)) <= 200);

    private sealed class Stats
    {
        public int Frames;
        public float WorstStart;
        public float WorstMuzzle;
        public float WorstAngle;
        public float WorstEnd;
        public float WorstFit;
        public float Moved;
        public FVector LastPawn;
        public string? Sample;
    }

    /// <summary>
    /// The local spy's drawn beam against his drawn pistol: the start must be the hand frame's 30 X - 4 Y + 8 Z (the
    /// pistol's actor frame at WeaponLoaded 2), a few uu behind the pistol mesh's FX bone; the end the dot's line.
    /// </summary>
    private string? CheckSpyBeam(int? pitch, bool crouched, bool moving = false)
    {
        if (!PitchNear(pitch) || Ticks < 20 || Pawn is not { } pawn || Pistol is not { bLaserOn: true } pistol)
        {
            return null;
        }

        if (pawn.bIsCrouched != crouched)
        {
            return Fail($"the spy is {(pawn.bIsCrouched ? "crouched" : "standing")}, this check is for the other stance");
        }

        if (_root!.SpyLasers is not { } lasers || _root.Pawns?.ViewOf(pawn)?.Attachments is not { } att)
        {
            return Fail("no spy laser view or no drawn pawn");
        }

        if (lasers.BeamsDrawn == 0 || !ReferenceEquals(lasers.BeamPawn, pawn))
        {
            return Fail($"the spy's own beam is not drawn (bLaserOn {pistol.bLaserOn}, beam actor {pistol.LaserBeam is not null})");
        }

        if (!att.TryGetWeapon(out Transform3D frame, out _) || !att.TryGetWeaponBone(BoneFx, out Transform3D fx))
        {
            return Fail("the drawn spy has no pistol model with an FX bone");
        }

        FCoords hand = PawnAttachments.ToCoords(frame);
        float up = pawn.WeaponLoaded == 7 ? -SWeaponAttaque.LaserStartUp : SWeaponAttaque.LaserStartUp;
        FVector want = hand.Origin + (hand.XAxis * SWeaponAttaque.LaserStartForward) - (hand.YAxis * SWeaponAttaque.LaserStartLeft) + (hand.ZAxis * up);
        FVector muzzle = UeSpace.ToUe(fx.Origin);
        FVector start = lasers.BeamStart;
        FVector dir = (lasers.BeamEnd - start).SafeNormal();
        float angle = Mathf.RadToDeg(MathF.Acos(Math.Clamp(dir | hand.XAxis.SafeNormal(), -1f, 1f)));
        // The dot sits 10 uu before the hit, on the simulation's line; the beam runs to the hit.
        // The drawn dot sits on the drawn beam's end (0.5 uu off the surface).
        float endGap = FVector.Dist(lasers.BeamEnd, lasers.DotAt);
        Stats st = _stats;
        if (st.Frames > 0)
        {
            st.Moved += FVector.Dist(pawn.Location, st.LastPawn);
        }

        st.LastPawn = pawn.Location;
        st.Frames++;
        st.WorstStart = MathF.Max(st.WorstStart, FVector.Dist(start, want));
        st.WorstMuzzle = MathF.Max(st.WorstMuzzle, FVector.Dist(start, muzzle));
        st.WorstAngle = MathF.Max(st.WorstAngle, angle);
        st.WorstEnd = MathF.Max(st.WorstEnd, endGap);
        // The drawn hand against the original's measured pistol place (CombatNatives.SpyPistolCoords, rule 14a).
        st.WorstFit = MathF.Max(st.WorstFit, FVector.Dist(hand.Origin, OpenScct.Game.Combat.CombatNatives.SpyPistolCoords(pawn, PC!.Rotation).Origin));
        st.Sample ??= string.Create(CultureInfo.InvariantCulture,
            $"pawn {pawn.Location} crouched {pawn.bIsCrouched} view {PC!.Rotation} hand {hand.Origin} pistol fit {Fit(pawn)} beam {start} -> {lasers.BeamEnd} want {want} FX {muzzle}");
        if (angle > 1f)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"the beam leaves the drawn pistol {angle:F1} deg off its hand's X axis: {st.Sample}"));
        }

        if (endGap > 1f)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"the drawn dot is {endGap:F1} uu from the drawn beam's end: {st.Sample}"));
        }

        if (FVector.Dist(start, want) > StartTolerance)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture,
                $"the beam starts {FVector.Dist(start, want):F1} uu from the drawn pistol's laser point (hand + 30 X - 4 Y + 8 Z) and {FVector.Dist(start, muzzle):F1} uu from its FX bone: {st.Sample}"));
        }

        if (st.Frames < CheckFrames || (moving && st.Moved < 40f))
        {
            return null;
        }

        GD.Print($"SVM_SMOKE {TestName} sample: {st.Sample}");
        return string.Create(CultureInfo.InvariantCulture,
            $"{st.Frames} frames: start within {st.WorstStart:F2} uu of the drawn laser point, {st.WorstMuzzle:F1} uu from the pistol's FX bone; beam off the hand's X axis by up to {st.WorstAngle:F1} deg; dot within {st.WorstEnd:F1} uu of the beam's end; drawn hand {st.WorstFit:F1} uu from the original's measured pistol place{(moving ? $", walked {st.Moved:F0} uu" : "")}");
    }

    /// <summary>The simulation's pistol frame (CombatNatives.SpyPistolCoords), for the log.</summary>
    private string Fit(SBasePawn pawn) =>
        OpenScct.Game.Combat.CombatNatives.SpyPistolCoords(pawn, PC!.Rotation).Origin.ToString();

    private void BotLaser()
    {
        _server.BotLaser = true;
        _server.KeepSpyAlive = true;
    }

    /// <summary>The merc bot's beam on the spy's screen: from the FX bone of the rifle drawn in the bot's hands.</summary>
    private string? CheckBotBeam()
    {
        if (_root!.MercLasers is not { } lasers)
        {
            return Fail("no merc laser view");
        }

        foreach ((SBasePawn bot, FVector start, FVector end, bool local) in lasers.Drawn)
        {
            if (local || _root.Pawns?.ViewOf(bot)?.Attachments is not { } att)
            {
                continue;
            }

            if (!att.TryGetWeaponBone(BoneFx, out Transform3D fx))
            {
                return Fail("the bot's drawn rifle has no FX bone");
            }

            FVector muzzle = UeSpace.ToUe(fx.Origin);
            float off = FVector.Dist(start, muzzle);
            Stats st = _stats;
            st.Frames++;
            st.WorstStart = MathF.Max(st.WorstStart, off);
            st.Sample ??= string.Create(CultureInfo.InvariantCulture, $"bot {bot.Location} wl {bot.WeaponLoaded} beam {start} -> {end} rifle FX {muzzle}");
            if (off > StartTolerance)
            {
                return Fail(string.Create(CultureInfo.InvariantCulture, $"the bot's beam starts {off:F1} uu from its drawn rifle's FX bone: {st.Sample}"));
            }

            if (st.Frames < CheckFrames)
            {
                return null;
            }

            GD.Print($"SVM_SMOKE {TestName} sample: {st.Sample}");
            return string.Create(CultureInfo.InvariantCulture, $"{st.Frames} frames: start within {st.WorstStart:F2} uu of the bot's drawn rifle FX bone, {FVector.Dist(start, end):F0} uu long");
        }

        return null;
    }

    /// <summary>The local merc's beam: his first-person rifle's FX bone pulled back 50 uu along the beam (rule 9).</summary>
    private string? CheckMercBeam(int pitch)
    {
        if (!PitchNear(pitch) || Ticks < 20 || _root!.MercLasers is not { } lasers || _root.FirstPerson is not { } fp)
        {
            return null;
        }

        if (!lasers.LocalVisible)
        {
            return Fail("the local merc's beam is not drawn");
        }

        int fxIndex = fp is { Shown: true, Model: { } m } ? m.Skeleton.FindBone("FX") : -1;
        if (fxIndex < 0 || fp.Model is not { } model)
        {
            return Fail("no first-person rifle with an FX bone");
        }

        FVector muzzle = UeSpace.ToUe((model.Skeleton.GlobalTransform * model.Skeleton.GetBoneGlobalPose(fxIndex)).Origin);
        FVector dir = (lasers.LocalEnd - muzzle).SafeNormal();
        FVector want = muzzle - (dir * K.LaserFireStartPullBack);
        float off = FVector.Dist(lasers.LocalStart, want);
        Stats st = _stats;
        st.Frames++;
        st.WorstStart = MathF.Max(st.WorstStart, off);
        st.Sample ??= string.Create(CultureInfo.InvariantCulture, $"eye {Session.View.Location} view {PC!.Rotation} FX {muzzle} beam {lasers.LocalStart} -> {lasers.LocalEnd}");
        if (off > StartTolerance)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"the merc's beam starts {off:F1} uu from his rifle's FX bone pulled back 50 uu: {st.Sample}"));
        }

        // What he sees: the stretch behind the muzzle is hidden by the rifle in the original (the weapon overlay).
        float drawn = FVector.Dist(lasers.DrawnFrom, muzzle);
        st.WorstMuzzle = MathF.Max(st.WorstMuzzle, drawn);
        if (drawn > StartTolerance)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"the merc's drawn beam begins {drawn:F1} uu from his rifle's muzzle: {st.Sample}"));
        }

        if (st.Frames < CheckFrames)
        {
            return null;
        }

        GD.Print($"SVM_SMOKE {TestName} sample: {st.Sample}");
        return string.Create(CultureInfo.InvariantCulture, $"{st.Frames} frames: start within {st.WorstStart:F2} uu of the FX bone pulled back 50 uu, drawn from within {st.WorstMuzzle:F2} uu of the muzzle");
    }

    // ---- the wall behind the merc --------------------------------------------------------------------------------

    /// <summary>Where the sandbox merc backed into the wall: east of the yard lane, facing east (yaw 0).</summary>
    private static readonly FVector WallSpot = new(-1112f, -2032f, -1036f);

    private int _still;
    private FVector _lastAt;
    private int _ammo = -1;
    private int _frags = -1;

    private void BackToWall()
    {
        // Test setup: the same jump on the server and on the client (the client's moves carry its own place), at the
        // spot the sandbox merc stopped at, repeated until he stands there.
        if (Pawn is { } pawn && (_frame == 1 || (_frame % 60 == 0 && MathF.Abs(pawn.Location.X - WallSpot.X) + MathF.Abs(pawn.Location.Y - WallSpot.Y) > 40f)))
        {
            _server.Teleport = WallSpot;
            _teleportAt = _frame;
        }

        if (_frame == _teleportAt + 2 && Pawn is { } p2 && PC is { } pc2)
        {
            p2.SetLocation(WallSpot);
            p2.Velocity = default;
            var face = new FRotator(0, 0, 0);
            p2.Rotation = face;
            pc2.Rotation = face;
            pc2.MyDesiredRotation = face;
            pc2.LocalMyDesiredRotation = face;
        }

        if (_frame > _teleportAt + 2)
        {
            AimPitch(0);
        }

        if (_frame == 90)
        {
            _keys!.Press("BT_Down");
        }
    }

    private int _teleportAt = -10;

    private string? AtWall()
    {
        if (_frame < 100 || Pawn is not { } pawn || !Walking)
        {
            return null;
        }

        _still = FVector.Dist(pawn.Location, _lastAt) < 0.2f ? _still + 1 : 0;
        _lastAt = pawn.Location;
        if (_still < 20)
        {
            return null;
        }

        float wall = _server.WallBehind;
        if (wall < 0f || wall > 45f)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture, $"stopped at {pawn.Location} but no wall within 45 uu behind the eye (trace {wall:F1})"));
        }

        return string.Create(CultureInfo.InvariantCulture, $"stopped at {pawn.Location}, yaw {PC!.Rotation.Yaw}, wall {wall:F1} uu behind the eye");
    }

    private SWeaponDefense? Rifle => Pawn?.MyWeapon as SWeaponDefense;

    private void Shoot()
    {
        if (_frame == 2)
        {
            _ammo = Rifle?.ClipAmmo ?? -1;
            _keys!.Tap("Fire");
        }
    }

    private string? Shot() =>
        _ammo > 0 && Rifle is { } rifle && rifle.ClipAmmo < _ammo ? $"clip {_ammo} -> {rifle.ClipAmmo}" : null;

    private void Throw()
    {
        if (_frame == 2)
        {
            _frags = _server.Frags;
            _keys!.Tap("AltFire");
        }
    }

    private string? Thrown() =>
        _frags >= 0 && _server.Frags > _frags ? $"frags thrown on the server {_frags} -> {_server.Frags}, first at {_server.FragAt}" : null;

    private string Describe() => _root?.Session is null ? "no session" : string.Create(CultureInfo.InvariantCulture,
        $"state={PC?.GetStateName()} rot={PC?.Rotation} wl={Pawn?.WeaponLoaded} spyLaser={Pistol?.bLaserOn} spyBeams={_root.SpyLasers?.BeamsDrawn} mercBeams={_root.MercLasers?.BeamsDrawn} {_server.Note}");

    private void Shot(string step)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(folder);
        Image image = _root.GetViewport().GetTexture().GetImage();
        string safe = string.Concat(step.Split(System.IO.Path.GetInvalidFileNameChars())).Replace(' ', '_');
        image.SavePng(System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{TestName}-{_shots++:D2}-{safe}.png")));
    }

    // ---- the server's thread ------------------------------------------------------------------------------------

    /// <summary>
    /// Runs on the listen server's thread before each world step. Once asked: holds the merc bot 300 uu in front of
    /// the spy, turned across his view, with its rifle drawn and its laser on (SGadTorcheLaser TorcheON's effect:
    /// bInUse and SPAWNDEFENSE.bLaserOn), and keeps the spy's health up so the bot cannot end the leg.
    /// </summary>
    private sealed class ServerHook
    {
        /// <summary>The wall check and the teleport for the local merc (the human player's pawn on the server).</summary>
        private void Watch(Level level)
        {
            foreach (SProjFrag f in level.AllActors<SProjFrag>())
            {
                if (_fragsSeen.Add(f))
                {
                    FragAt = f.Location;
                }
            }

            Frags = _fragsSeen.Count;

            foreach (SPAWNDEFENSE merc in level.AllActors<SPAWNDEFENSE>())
            {
                // The merc leg's only merc is the player (its bot is a spy).
                if (merc.bDeleteMe || merc.Controller is not SPlayerController pc)
                {
                    continue;
                }

                if (Teleport is { } to)
                {
                    Teleport = null;
                    merc.SetLocation(to);
                    merc.Velocity = default;
                    var face = new FRotator(0, 0, 0);
                    merc.Rotation = face;
                    pc.Rotation = face;
                    pc.MyDesiredRotation = face;
            pc.LocalMyDesiredRotation = face;
                }

                FVector eye = merc.Location + new FVector(0f, 0f, merc.EyeHeight);
                FVector back = -(new FRotator(0, pc.MyDesiredRotation.Yaw, 0).Vector());
                WallBehind = GadgetWorld.Line(merc, eye + (back * 200f), eye, ETraceFlags.TRACE_World, out FCheckResult hit)
                    ? FVector.Dist(hit.Location, eye)
                    : -1f;
            }
        }

        private readonly HashSet<Actor> _fragsSeen = new(ReferenceEqualityComparer.Instance);

        private FVector? _spot;

        public volatile bool BotLaser;

        public volatile bool KeepSpyAlive;

        public volatile string Note = "";

        /// <summary>Set by the main thread: put the local player's pawn here once, facing east.</summary>
        public FVector? Teleport;

        /// <summary>How far behind the player's eye the world is, along his facing (-1: nothing within 200 uu).</summary>
        public volatile float WallBehind = -1f;

        public volatile int Frags;

        public FVector FragAt;

        public void Reset()
        {
            BotLaser = false;
            KeepSpyAlive = false;
            _spot = null;
            Note = "";
            Teleport = null;
            WallBehind = -1f;
            Frags = 0;
            _fragsSeen.Clear();
        }

        public void OnServerTick(OpenScct.Server.ServerHost host)
        {
            Level level = host.World.Level;
            Watch(level);
            if (!BotLaser)
            {
                return;
            }

            SPawnAttaque? spy = null;
            SPAWNDEFENSE? bot = null;
            foreach (SBasePawn p in level.AllActors<SBasePawn>())
            {
                if (p.bDeleteMe || p.Health <= 0)
                {
                    continue;
                }

                if (p is SPawnAttaque a)
                {
                    spy ??= a;
                }
                else if (p is SPAWNDEFENSE d)
                {
                    bot ??= d;
                }
            }

            if (spy is null || bot is null)
            {
                Note = "server: no spy or no bot";
                return;
            }

            if (KeepSpyAlive)
            {
                spy.Health = 100;
            }

            int yaw = spy.Controller?.Rotation.Yaw ?? spy.Rotation.Yaw;
            if (_spot is null)
            {
                FVector eye = spy.Location + new FVector(0f, 0f, spy.EyeHeight);
                for (float d = 350f; d >= 150f && _spot is null; d -= 50f)
                {
                    FVector at = spy.Location + (new FRotator(0, yaw, 0).Vector() * d);
                    if (!GadgetWorld.Line(spy, at + new FVector(0f, 0f, spy.EyeHeight), eye, ETraceFlags.TRACE_World, out _))
                    {
                        _spot = at;
                    }
                }

                if (_spot is null)
                {
                    Note = "server: no clear spot in front of the spy";
                    return;
                }
            }

            FVector spot = _spot.Value;
            spot.Z = spy.Location.Z - spy.CollisionHeight + bot.CollisionHeight + 1f;
            if (FVector.Dist(bot.Location, spot) > 30f)
            {
                bot.SetLocation(spot);
            }

            bot.Velocity = new FVector(0f, 0f, bot.Velocity.Z);
            bot.Acceleration = default;
            // Across the spy's view, so the beam crosses the picture.
            bot.Rotation = new FRotator(0, yaw + 16384, 0);
            if (bot.Controller is { } c)
            {
                c.Rotation = bot.Rotation;
                if (c is SPlayerController pc)
                {
                    pc.MyDesiredRotation = bot.Rotation;
                }
            }

            foreach (SGadTorcheLaser laser in level.AllActors<SGadTorcheLaser>())
            {
                if (!laser.bDeleteMe && ReferenceEquals(laser.Instigator, bot))
                {
                    laser.bInUse = true;
                }
            }

            bot.bLaserOn = true;
            Note = string.Create(CultureInfo.InvariantCulture, $"server: bot at {bot.Location} wl {bot.WeaponLoaded}");
        }
    }
}
