using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScctGame.Hud.Magma;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-reticle</c>: the merc's reticle is drawn where his bullets and frags go
/// (spec/hud/merc-reticle.md rule 3a, spec/equipment/merc-rifle.md rule 10, spec/equipment/grenade-throw.md). TestRange
/// as the merc, launched as the launchers play it (host thread, wall clock, HUD). The server puts the merc in the
/// shooting lane at a distance from the berm; the keys turn him with the mouse, crouch, strafe and fire single shots
/// (left mouse), then select the frag (1) and throw it (right mouse held). For every shot the drawn reticle's centre
/// dot of the frame the player clicked on (the HUD's own draw list) and the drawn camera of that frame are kept; the
/// bullet's impact mark the client draws is then seen from that camera: the angle between the ray through the dot and
/// the ray to the mark must stay inside the shot's spread (the dispersion of the moment, a square of side Accuracy x
/// 1000 at 15000 uu), plus one page unit of the reticle's truncated placement. The frag's first bounce is the free
/// flight of the client's frag (its first two frames and the zone's gravity) met with the berm's face; it is
/// reported against the dot the same way and must pass inside the reticle's ring. With a window and
/// <c>SVM_PLAY_SHOTS</c> it saves the window before and after every shot.
/// </summary>
public sealed class PlayReticleSmoke : SmokeTest
{
    public const string TestName = "play-reticle";

    private const double SpawnTimeoutSeconds = 120.0;
    private const int TeardownFrames = 6;

    /// <summary>The berm's face toward the firing line (tools/testmap/layout.py LaneBerm).</summary>
    private const float BermX = 3584f;

    /// <summary>The reticle's ring (MERC_reticule cercle_rond, 35 page units across) as a half width in page units.</summary>
    private const float RingHalf = 17.5f;

    /// <summary>The original's camera over the floor, standing and crouched (sandbox capture).</summary>
    private const float OriginalStandingEye = 144.0f;

    private const float OriginalCrouchedEye = 89.0f;

    /// <summary>The original's pawn rests 1 uu over the floor, ours as its collision puts it.</summary>
    private const float EyeTolerance = 2f;

    /// <summary>The original's frag spawn from the eye along the aim (sandbox capture, four throws).</summary>
    private const float OriginalFragForward = 25.3f;

    private const float OriginalFragRight = 11f;

    private const float OriginalFragUp = -12.9f;

    /// <summary>The falling physics' gravity: the velocity takes half of it each step (decoded; the original's frags lose 750 uu/s a second).</summary>
    private const float OriginalFragGravity = -1500f;

    /// <summary>The frag's collision radius and height against the berm and the floor (SProjFrag defaults).</summary>
    private const float FragRadius = 2f;

    /// <summary>How far the drawn frag may stray from the original's path: the whole-unit rounding of the spawn and
    /// the client's copy (it starts from the replicated spawn and velocity, both rounded on the wire).</summary>
    private const float FragPathTolerance = 4f;

    /// <summary>
    /// The original draws its centre dot a unit up and left of the aim point (sandbox capture), and the placement is
    /// truncated to whole units: a shot may land this many page units off the dot beyond its spread.
    /// </summary>
    private const float DotSlackUnits = 1.5f;

    private sealed record Case(string Label, float Distance, float LookX, float LookY, bool Crouch, bool Strafe);

    private static readonly Case[] Cases =
    [
        new("near 200 straight", 200f, 0f, 0f, false, false),
        new("near 200 left", 200f, -90f, 0f, false, false),
        new("near 200 low", 200f, 0f, 40f, false, false),
        new("mid 800 straight", 800f, 0f, 0f, false, false),
        new("mid 800 right high", 800f, 60f, -25f, false, false),
        new("far 1900 straight", 1900f, 0f, 0f, false, false),
        new("far 1900 floor", 1900f, 20f, 70f, false, false),
        new("crouched 600", 600f, 0f, 0f, true, false),
        new("crouched 600 high", 600f, -30f, -30f, true, false),
        new("strafing 600", 600f, 0f, 0f, false, true),
    ];

    private sealed class Snap
    {
        public Vector2? Dot;
        public Vector2 DotPage;
        public Vector2 Screen;
        public Vector3 Origin;
        public Vector3 DotRay;
        public Vector3 Forward;
        public Vector2 PageScale;
        public int Clip;
        public float Accuracy;
        public FVector CamUe;
        public FVector EyeUe;
        public float Floor;
        public bool Crouched;
        public FRotator Aim;
        public FRotator View;
        public bool ReticleShown;
    }

    private readonly List<string> _lines = [];
    private readonly List<string> _fails = [];
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private TestRange.Station? _lane;
    private int _case = -1;
    private int _phase;
    private double _phaseStart;
    private Snap? _prev;
    private Snap? _shotSnap;
    private int[] _serials = [];
    private bool _crouchTapped;
    private SmokeResult _final;
    private int _teardown = -1;

    // Frag
    private int _fragPhase;
    private SProjFrag? _frag;
    private readonly List<(double T, FVector At, FVector V)> _fragTrack = [];

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
            _lane = TestRange.Stations()["Lane"];
            _root = TestRange.Start(context, PlaySide.Merc, bots: 0);
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

        SPlayerController? pc = session.LocalController;
        if (_keys is null)
        {
            if (!TestRange.Walking(pc) || session.LocalPawn?.WeaponLoaded == 0)
            {
                return context.ElapsedSeconds > SpawnTimeoutSeconds ? Finish(SmokeResult.Fail("the merc was not drawn in a walking state with his rifle out")) : SmokeResult.Running;
            }

            _keys = KeyPilot.Attach(_root);
            RememberMarks(session);
            return SmokeResult.Running;
        }

        _keys.Update();
        if (!_keys.Ready || pc is null)
        {
            return SmokeResult.Running;
        }

        Snap now = TakeSnap(session, pc);
        Snap? before = _prev;
        _prev = now;
        double t = context.ElapsedSeconds - _phaseStart;
        if (_case < Cases.Length)
        {
            return Shots(context, session, pc, now, before, t);
        }

        return Frag(context, session, pc, now, t);
    }

    private SmokeResult Shots(SmokeContext context, PlaySession session, SPlayerController pc, Snap now, Snap? before, double t)
    {
        switch (_phase)
        {
            case 0:
                // Next case: put the merc in the lane at its distance, facing the berm.
                _case++;
                if (_case >= Cases.Length)
                {
                    _phase = 0;
                    _phaseStart = context.ElapsedSeconds;
                    return SmokeResult.Running;
                }

                Case c0 = Cases[_case];
                TestRange.Place(session, _ => session.ServerController, new FVector(BermX - c0.Distance, _lane!.Spot.Y, 0f), 0);
                Next(context, 1);
                return SmokeResult.Running;
            case 1:
                if (t < 1.0)
                {
                    return SmokeResult.Running;
                }

                // The client's view and aim follow the player's mouse: set them level toward the berm once, as a
                // player would have turned there, then the mouse does the rest.
                TestRange.Face(pc, 0);
                Next(context, 2);
                return SmokeResult.Running;
            case 2:
                if (t < 0.8)
                {
                    return SmokeResult.Running;
                }

                // The stance with the crouch key (a toggle), checked on the drawn pawn before the shot.
                Case c2 = Cases[_case];
                bool crouched = session.LocalPawn?.bIsCrouched ?? false;
                if (c2.Crouch != crouched)
                {
                    if (t > 4.0)
                    {
                        _fails.Add($"{c2.Label}: the crouch key did not {(c2.Crouch ? "crouch" : "stand")} the merc");
                        Next(context, 0);
                        return SmokeResult.Running;
                    }

                    if (!_crouchTapped)
                    {
                        _keys!.Tap("bCrouch");
                        _crouchTapped = true;
                    }

                    return SmokeResult.Running;
                }

                if (_crouchTapped && t < 2.0)
                {
                    return SmokeResult.Running;
                }

                _crouchTapped = false;
                _keys!.Look(c2.LookX, c2.LookY);
                if (c2.Strafe)
                {
                    _keys.Press("BT_Left");
                }

                Next(context, 3);
                return SmokeResult.Running;
            case 3:
                // Settle the spread (TimeAccReset) before the shot; a strafe keeps moving.
                if (t < 1.0)
                {
                    return SmokeResult.Running;
                }

                TestRange.Shot(_root, TestName, context.ElapsedSeconds, $"{_case}-aim-{Cases[_case].Label}");
                _shotSnap = null;
                RememberMarks(session);
                _keys!.Tap("bFire");
                Next(context, 4);
                return SmokeResult.Running;
            case 4:
                // The frame the player clicked on is the one drawn before the tick that fired.
                if (_shotSnap is null && before is not null && now.Clip < before.Clip)
                {
                    _shotSnap = before;
                    _shotSnap.Accuracy = MathF.Max(before.Accuracy, now.Accuracy);
                }

                if (_shotSnap is not null && NewMark(session) is { } mark)
                {
                    _keys!.Release("BT_Left");
                    Judge(Cases[_case].Label, _shotSnap, mark, isFrag: false);
                    JudgeEye(Cases[_case], _shotSnap);
                    TestRange.Shot(_root, TestName, context.ElapsedSeconds, $"{_case}-hit-{Cases[_case].Label}");
                    Next(context, 0);
                    return SmokeResult.Running;
                }

                if (t > 3.0)
                {
                    _keys!.Release("BT_Left");
                    _fails.Add($"{Cases[_case].Label}: {(_shotSnap is null ? "the rifle did not fire" : "no impact mark appeared on the client")}");
                    Next(context, 0);
                }

                return SmokeResult.Running;
        }

        return SmokeResult.Running;
    }

    private SmokeResult Frag(SmokeContext context, PlaySession session, SPlayerController pc, Snap now, double t)
    {
        const float distance = 260f;
        switch (_fragPhase)
        {
            case 0:
                if (session.LocalPawn?.bIsCrouched ?? false)
                {
                    _keys!.Tap("bCrouch");
                }

                TestRange.Place(session, _ => session.ServerController, new FVector(BermX - distance, _lane!.Spot.Y, 0f), 0);
                NextFrag(context, 1);
                return SmokeResult.Running;
            case 1:
                if (t < 1.0)
                {
                    return SmokeResult.Running;
                }

                TestRange.Face(pc, 0);
                _keys!.Tap("IK_1");
                NextFrag(context, 2);
                return SmokeResult.Running;
            case 2:
                if (t < 1.5)
                {
                    return SmokeResult.Running;
                }

                if (session.LocalPawn?.SelectedGadget is not SGadFragGrenade)
                {
                    return Done($"the frag was not selected with 1 (selected {session.LocalPawn?.SelectedGadget?.GetType().Name ?? "none"})");
                }

                _shotSnap = now;
                TestRange.Shot(_root, TestName, context.ElapsedSeconds, "frag-aim");
                _keys!.Press("IK_RightMouse");
                NextFrag(context, 3);
                return SmokeResult.Running;
            case 3:
                // The aim the throw goes along is the one of the frame before the right mouse lets go.
                if (t < 0.3)
                {
                    _shotSnap = now;
                    return SmokeResult.Running;
                }

                _keys!.Release("IK_RightMouse");
                NextFrag(context, 4);
                return SmokeResult.Running;
            default:
                TrackFrag(session, context.ElapsedSeconds);
                if (_frag is { bDeleteMe: true } || t > 4.0)
                {
                    JudgeFrag(session);
                    return Done(null);
                }

                return SmokeResult.Running;
        }
    }

    private void Next(SmokeContext context, int phase)
    {
        _phase = phase;
        _phaseStart = context.ElapsedSeconds;
    }

    private void NextFrag(SmokeContext context, int phase)
    {
        _fragPhase = phase;
        _phaseStart = context.ElapsedSeconds;
    }

    private SmokeResult Done(string? failure)
    {
        if (failure is not null)
        {
            _fails.Add(failure);
        }

        var sb = new StringBuilder();
        foreach (string l in _lines)
        {
            GD.Print("SVM_RETICLE " + l);
            sb.Append(l).Append('\n');
        }

        return Finish(_fails.Count == 0
            ? SmokeResult.Pass($"{_lines.Count} checks on the drawn reticle\n{sb}")
            : SmokeResult.Fail(string.Join("\n", _fails) + "\n" + sb));
    }

    private Snap TakeSnap(PlaySession session, SPlayerController pc)
    {
        var s = new Snap();
        Camera3D? cam = _root?.View?.Camera;
        Viewport vp = _root!.GetViewport();
        s.Screen = vp.GetVisibleRect().Size;
        if (_root.Hud?.Merc is { } merc && merc.DrawList.Items.Count > 0 && ReferenceEquals(_root.Hud.ActivePage, merc))
        {
            foreach (MagmaDrawItem item in merc.DrawList.Items)
            {
                if (item.Path.EndsWith("rond02", StringComparison.OrdinalIgnoreCase) && item.Points.Length > 0)
                {
                    Vector2 sum = Vector2.Zero;
                    foreach (Vector2 p in item.Points)
                    {
                        sum += p;
                    }

                    s.Dot = sum / item.Points.Length;
                    s.DotPage = new Vector2((item.PageRect.L + item.PageRect.R) * 0.5f, (item.PageRect.T + item.PageRect.B) * 0.5f);
                    s.ReticleShown = true;
                }
            }

            s.PageScale = new Vector2(merc.Renderer.S, merc.Renderer.S);
        }

        if (cam is not null && cam.IsInsideTree())
        {
            Vector2 at = s.Dot ?? s.Screen * 0.5f;
            s.Origin = cam.ProjectRayOrigin(at);
            s.DotRay = cam.ProjectRayNormal(at);
            s.Forward = -cam.GlobalBasis.Z;
            s.CamUe = UeSpace.ToUe(cam.GlobalPosition);
        }

        SBasePawn? pawn = session.LocalPawn;
        if (pawn?.MyWeapon is SWeaponDefense rifle)
        {
            s.Clip = rifle.ClipAmmo;
            s.Accuracy = rifle.GetAccuracy();
        }

        if (pawn is not null)
        {
            s.EyeUe = pawn.Location + pawn.WalkBob + new FVector(0f, 0f, pawn.EyeHeight);
            s.Floor = pawn.Location.Z - pawn.CollisionHeight;
            s.Crouched = pawn.bIsCrouched;
        }

        s.Aim = pc.MyDesiredRotation;
        s.View = pc.Rotation;
        return s;
    }

    /// <summary>Remembers every impact slot's serial, so the next mark is the slot whose serial changes.</summary>
    private void RememberMarks(PlaySession session)
    {
        IReadOnlyList<ImpactMark> slots = ImpactTable.Of(session.Client.World.Level).Slots;
        if (_serials.Length != slots.Count)
        {
            _serials = new int[slots.Count];
        }

        for (int i = 0; i < slots.Count; i++)
        {
            _serials[i] = slots[i].Serial;
        }
    }

    private FVector? NewMark(PlaySession session)
    {
        IReadOnlyList<ImpactMark> slots = ImpactTable.Of(session.Client.World.Level).Slots;
        for (int i = 0; i < slots.Count && i < _serials.Length; i++)
        {
            if (slots[i].Live && slots[i].Serial != _serials[i])
            {
                FVector at = slots[i].Location;
                RememberMarks(session);
                return at;
            }
        }

        return null;
    }

    /// <summary>
    /// The angle (degrees) between the ray through the drawn dot and the ray to <paramref name="hit"/>, split into
    /// the camera's right and up axes; with the same for the screen centre.
    /// </summary>
    private void Judge(string label, Snap s, FVector hit, bool isFrag)
    {
        if (!s.ReticleShown || s.Dot is not { } dot)
        {
            _fails.Add($"{label}: the reticle was not drawn on the frame of the shot");
            return;
        }

        Vector3 to = (UeSpace.ToGodot(hit) - s.Origin).Normalized();
        (float right, float up) = Split(s, to, s.DotRay);
        (float cRight, float cUp) = Split(s, to, s.Forward);
        float distance = FVector.Dist(hit, s.CamUe);

        // One page unit of the truncated placement, as an angle at the dot (the HUD's page height is 480 units over
        // the vertical field of view).
        float unit = PageUnitDegrees(s);
        float spread = isFrag ? 0f : MathF.Atan(s.Accuracy * K.SpreadScale * 0.5f / K.TraceLength) * (180f / MathF.PI);
        float parallax = MathF.Atan(FVector.Dist(s.CamUe, s.EyeUe) / MathF.Max(distance, 1f)) * (180f / MathF.PI);
        float tol = isFrag ? (RingHalf * unit) : spread + (DotSlackUnits * unit) + parallax;
        bool ok = isFrag || (MathF.Abs(right) <= tol && MathF.Abs(up) <= tol);
        string line = string.Create(CultureInfo.InvariantCulture,
            $"{(isFrag ? "info" : ok ? "ok  " : "FAIL")} {label}: hit {Fmt(hit)} at {distance:F0} uu; off the dot right {right:+0.00;-0.00} up {up:+0.00;-0.00} deg ({(isFrag ? $"the reticle's ring is {tol:F2}" : $"tolerance {tol:F2}: spread {spread:F2}, unit {unit:F2}, eye-camera {parallax:F2}")}); off the screen centre right {cRight:+0.00;-0.00} up {cUp:+0.00;-0.00}; dot {dot.X:F1},{dot.Y:F1} (page {s.DotPage.X:F1},{s.DotPage.Y:F1}) of {s.Screen.X:F0}x{s.Screen.Y:F0}; aim {s.Aim.Pitch}/{s.Aim.Yaw} view {s.View.Pitch}/{s.View.Yaw}; camera {Fmt(s.CamUe)} eye {Fmt(s.EyeUe)}");
        _lines.Add(line);
        if (!ok)
        {
            _fails.Add(line);
        }
    }

    /// <summary>
    /// The camera's height over the floor against the original's (sandbox capture: the eye 144.0 over the floor
    /// standing, 89.0 crouched, the camera on it), when the merc stands still.
    /// </summary>
    private void JudgeEye(Case c, Snap s)
    {
        if (c.Strafe)
        {
            return;
        }

        float want = s.Crouched ? OriginalCrouchedEye : OriginalStandingEye;
        float got = s.CamUe.Z - s.Floor;
        bool ok = s.Crouched == c.Crouch && MathF.Abs(got - want) <= EyeTolerance;
        string line = string.Create(CultureInfo.InvariantCulture,
            $"{(ok ? "ok  " : "FAIL")} {c.Label}: camera {got:F1} over the floor {(s.Crouched ? "crouched" : "standing")} (original {want:F1} +- {EyeTolerance})");
        _lines.Add(line);
        if (!ok)
        {
            _fails.Add(line);
        }
    }

    private static (float Right, float Up) Split(Snap s, Vector3 to, Vector3 reference)
    {
        // Angles of 'to' from 'reference' about the camera's up (yaw) and right (pitch) axes.
        Vector3 fwd = s.Forward.Normalized();
        Vector3 upAxis = Vector3.Up;
        Vector3 rightAxis = fwd.Cross(upAxis).Normalized();
        upAxis = rightAxis.Cross(fwd).Normalized();
        float Yaw(Vector3 v) => MathF.Atan2(v.Dot(rightAxis), v.Dot(fwd));
        float Pitch(Vector3 v) => MathF.Atan2(v.Dot(upAxis), v.Dot(fwd));
        const float deg = 180f / MathF.PI;
        return ((Yaw(to) - Yaw(reference)) * deg, (Pitch(to) - Pitch(reference)) * deg);
    }

    private float PageUnitDegrees(Snap s)
    {
        // The page's 480 units span the screen's height, which the camera's vertical field of view spans.
        float fov = _root?.View?.Camera.Fov ?? 60f;
        return fov / MagmaRenderer.PageHeight;
    }

    private void TrackFrag(PlaySession session, double now)
    {
        Level level = session.Client.World.Level;
        if (_frag is null)
        {
            foreach (SProjFrag f in level.AllActors<SProjFrag>())
            {
                if (!f.bDeleteMe)
                {
                    _frag = f;
                    break;
                }
            }
        }

        if (_frag is { bDeleteMe: false } a && (_fragTrack.Count == 0 || _fragTrack[^1].At != a.Location))
        {
            _fragTrack.Add((now, a.Location, a.Velocity));
        }
    }

    private void JudgeFrag(PlaySession session)
    {
        if (_fragTrack.Count < 2 || _shotSnap is null)
        {
            _fails.Add($"frag: {(_fragTrack.Count == 0 ? "no frag appeared on the client" : "the frag was seen for one frame only")}");
            return;
        }

        // The original's throw from the frame before the release (sandbox capture, local/reticle/capture2.py): from
        // the eye, 25.3 forward, 11.0 right and 12.9 down along the aim (the rifle's FX bone plus 40 along the aim),
        // rounded to whole units; the aim cut to multiples of 256 (Reloaded's ThrowGren); then its falling physics,
        // whose velocity loses 750 uu/s per second, ticked at 60 Hz. The drawn frag must follow that path.
        FRotator aim = _shotSnap.Aim;
        aim.GetAxes(out FVector ax, out FVector ay, out FVector az);
        FVector o = _shotSnap.EyeUe + (ax * OriginalFragForward) + (ay * OriginalFragRight) + (az * OriginalFragUp);
        o = new FVector(MathF.Round(o.X), MathF.Round(o.Y), MathF.Round(o.Z));
        var quantised = new FRotator(aim.Pitch & 0xFF00, aim.Yaw & 0xFF00, 0);
        float speed = LaunchSpeed();
        FVector v = quantised.Vector() * speed;
        var path = new List<FVector> { o };
        const float dt = 1f / 60f;
        FVector p = o;
        FVector? bounce = null;
        for (int i = 0; i < 240 && bounce is null; i++)
        {
            v += new FVector(0f, 0f, OriginalFragGravity * 0.5f * dt);
            FVector next = p + (v * dt);
            if (next.X >= BermX - FragRadius || next.Z <= FragRadius)
            {
                float f = next.X >= BermX - FragRadius ? (BermX - FragRadius - p.X) / (next.X - p.X) : (p.Z - FragRadius) / (p.Z - next.Z);
                bounce = p + ((next - p) * f);
            }

            p = next;
            path.Add(p);
        }

        // The drawn frag against that path, up to its first bounce (the first sample whose velocity turned).
        float worst = 0f;
        int compared = 0;
        for (int k = 0; k < _fragTrack.Count; k++)
        {
            (double _, FVector at, FVector vel) = _fragTrack[k];
            if (vel.X < 0f || (k > 0 && vel.Z > _fragTrack[k - 1].V.Z + 1f))
            {
                break;
            }

            worst = MathF.Max(worst, DistanceToPath(path, at));
            compared++;
        }

        if (compared < 3)
        {
            _fails.Add($"frag path: only {compared} drawn samples before the first bounce");
        }

        FVector hit = bounce ?? p;
        bool ok = worst <= FragPathTolerance;
        string line = string.Create(CultureInfo.InvariantCulture,
            $"{(ok ? "ok  " : "FAIL")} frag path: the drawn frag keeps within {worst:F1} uu (tolerance {FragPathTolerance}) of the original's throw from {Fmt(o)} at {speed:F0} uu/s along pitch {quantised.Pitch} yaw {quantised.Yaw}, first bounce {Fmt(hit)}; {compared} drawn samples from {Fmt(_fragTrack[0].At)}");
        _lines.Add(line);
        if (!ok)
        {
            _fails.Add(line);
        }

        // Where that bounce sits against the reticle (reported: a frag arcs under the aim the reticle shows).
        Judge("frag first bounce", _shotSnap, hit, isFrag: true);
    }

    /// <summary>The frag's launch speed from the drawn samples before the first bounce (horizontal speed over the
    /// horizontal share of the quantised aim, which gravity does not change).</summary>
    private float LaunchSpeed()
    {
        FRotator aim = _shotSnap!.Aim;
        var q = new FRotator(aim.Pitch & 0xFF00, aim.Yaw & 0xFF00, 0);
        FVector d = q.Vector();
        float h = MathF.Sqrt((d.X * d.X) + (d.Y * d.Y));
        FVector v = _fragTrack[0].V;
        return MathF.Sqrt((v.X * v.X) + (v.Y * v.Y)) / MathF.Max(h, 1e-3f);
    }

    private static float DistanceToPath(List<FVector> path, FVector at)
    {
        float best = float.MaxValue;
        for (int i = 1; i < path.Count; i++)
        {
            FVector a = path[i - 1], b = path[i];
            FVector ab = b - a;
            float len2 = ab | ab;
            float t = len2 > 0f ? Math.Clamp(((at - a) | ab) / len2, 0f, 1f) : 0f;
            best = MathF.Min(best, FVector.Dist(a + (ab * t), at));
        }

        return best;
    }

    private static string Fmt(FVector v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:F1},{v.Y:F1},{v.Z:F1})");

    private SmokeResult Finish(SmokeResult result)
    {
        _final = result;
        _teardown = 0;
        _keys?.ReleaseAll();
        _root?.QueueFree();
        return SmokeResult.Running;
    }
}
