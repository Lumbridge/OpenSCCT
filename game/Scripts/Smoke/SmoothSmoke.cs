using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=smooth</c>: what the player sees from frame to frame when the screen runs faster than the 60 Hz
/// simulation. The merc plays SteeE as the launchers run it (wall clock, listen server on its own thread, HUD), and
/// <see cref="KeyPilot"/> moves the mouse and presses the scope keys as Godot input events. For each frame cap of
/// SVM_SMOOTH_CAPS (default "0,144,60"; 0 is uncapped with vertical sync off) the <see cref="SmoothRecorder"/> reads
/// the drawn camera, the FOV, the scope sights and the view model after every view has drawn, and the smoke checks:
/// <list type="bullet">
/// <item>look: a steady mouse turn moves the drawn camera every frame (no frame holds still between ticks);</item>
/// <item>mouse latency: a single mouse move is drawn in full in the frame it reaches the game (no extra frame);</item>
/// <item>scope zoom: the FOV ramp of a zoom step changes every frame while it runs;</item>
/// <item>scope sights: while the merc turns in the scope, the sights move without a tick-rate saw tooth;</item>
/// <item>view model: the scope's raise and lower clips (channel 6) advance every frame while they play;</item>
/// <item>camera network: a steady turn of a network camera moves the drawn cam every frame at an even speed.</item>
/// </list>
/// Frames over 20 ms are listed with what ran in them (simulation, views, garbage collections). SVM_SMOOTH_CSV names a
/// folder under local/ for the per-frame curves; with a window and SVM_PLAY_SHOTS the frames at each step's end are
/// saved. Run it with a window: without one the sights check is only reported.
/// </summary>
public sealed class SmoothSmoke : SmokeTest
{
    public const string TestName = "smooth";

    /// <summary>Frames longer than this are listed as hitches.</summary>
    public const double HitchMs = 20.0;

    /// <summary>The largest sights saw tooth allowed: the change of the per-frame sights motion, in canvas units.</summary>
    public const float SightsJitterLimit = 1.5f;

    private const float LookPixelsPerSecond = 500f;
    private const float ScopeLookPixelsPerSecond = 250f;
    private const float ImpulsePixels = 24f;

    /// <summary>
    /// A steady cam turn: the cam ignores a tick's turn under its dead zone (K.CamInputDeadZone, about 650 pixels a
    /// second at 60 Hz), and its yaw window is narrow.
    /// </summary>
    private const float CamLookPixelsPerSecond = 1100f;

    private const float CamWindPixelsPerSecond = 4000f;

    /// <summary>The largest fastest-over-median per-second turn speed of a steady cam turn (a tick-rate beat is about 1.5).</summary>
    public const double CamLookSpreadLimit = 1.3;
    private const int Impulses = 6;
    private const double ImpulseGap = 0.3;
    private const int TeardownFrames = 4;
    private const int MaxHitchLines = 8;

    private readonly int[] _caps;
    private readonly List<Phase> _phases = new();
    private readonly List<string> _notes = new();
    private readonly HashSet<int> _shotFrames = new();
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private SmoothRecorder? _recorder;
    private int _phase = -1;
    private double _phaseStart;
    private int _phaseFrames;
    private int _shots;
    private int _teardown = -1;
    private SmokeResult _final;
    private Vsync _vsync;

    private enum Vsync
    {
        Untouched,
        Saved,
    }

    private DisplayServer.VSyncMode _savedVsync;
    private int _savedMaxFps;

    public SmoothSmoke()
    {
        string? caps = System.Environment.GetEnvironmentVariable("SVM_SMOOTH_CAPS");
        _caps = string.IsNullOrWhiteSpace(caps)
            ? [0, 144, 60]
            : caps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => int.Parse(c, CultureInfo.InvariantCulture)).ToArray();
        foreach (int cap in _caps)
        {
            AddPhases(cap);
        }
    }

    public override string Name => TestName;

    private sealed record Phase(string Label, int Cap, double MinSeconds, double MaxSeconds, Action<SmoothSmoke, double>? Act, Func<SmoothSmoke, double, bool>? Done);

    private void AddPhases(int cap)
    {
        _phases.Add(new Phase("settle", cap, 0.8, 0.8, (s, _) => s.SetCap(cap), null));
        _phases.Add(new Phase("look", cap, 1.2, 1.2, (s, _) => s.LookSteady(LookPixelsPerSecond), null));
        _phases.Add(new Phase("rest", cap, 0.4, 0.4, null, null));
        _phases.Add(new Phase("impulse", cap, Impulses * ImpulseGap, Impulses * ImpulseGap, (s, t) => s.Impulse(t), null));
        _phases.Add(new Phase("scope in", cap, 1.2, 4.0, (s, _) => s.TapOnce("IK_B"), (s, t) => s.Scoped && s.Pc?.FovAngle < 31f && t > 1.2));
        _phases.Add(new Phase("zoom in", cap, 0.8, 0.8, (s, _) => s.TapOnce("IK_PageUp"), null));
        _phases.Add(new Phase("scope look", cap, 1.2, 1.2, (s, _) => s.LookSteady(ScopeLookPixelsPerSecond), null));
        _phases.Add(new Phase("zoom out", cap, 0.8, 0.8, (s, _) => s.TapOnce("IK_PageDown"), null));
        _phases.Add(new Phase("scope out", cap, 1.0, 4.0, (s, _) => s.TapOnce("IK_B"), (s, t) => !s.Scoped && t > 1.0));

        // The camera network (key 3 selects it, the right mouse button enters and leaves): a steady turn of the cam.
        _phases.Add(new Phase("camnet in", cap, 1.5, 5.0, (s, t) => s.CamNetIn(t), (s, t) => s.InCam && t > 1.5));
        // Zoomed in (the turn scales with the FOV) and turned to the right end of the cam's yaw window first, so the
        // steady turn to the left has the whole window.
        _phases.Add(new Phase("camnet zoom", cap, 1.8, 1.8, (s, t) => s.HoldFor("IK_PageUp", t, 1.6), null));
        _phases.Add(new Phase("camnet right", cap, 0.8, 0.8, (s, _) => s.LookSteady(CamWindPixelsPerSecond), null));
        _phases.Add(new Phase("camnet look", cap, 0.9, 0.9, (s, _) => s.LookSteady(-CamLookPixelsPerSecond), null));
        _phases.Add(new Phase("camnet out", cap, 1.0, 4.0, (s, _) => s.TapOnce("IK_RightMouse"), (s, t) => !s.InCam && t > 1.0));
    }

    private SPlayerController? Pc => _root?.Session?.LocalController;

    private bool Scoped => Pc?.GetStateName().ToString() == "s_Binoculars_Def";

    private bool InCam => Pc is { } pc && OpenScct.Game.Equipment.GadgetStates.InStickyCamViewState(pc);

    private bool _camKeyDown;

    private void HoldFor(string key, double t, double seconds)
    {
        if (_phaseFrames == 0)
        {
            _keys!.Press(key);
        }
        else if (t >= seconds)
        {
            _keys!.Release(key);
        }
    }

    private void CamNetIn(double t)
    {
        if (_phaseFrames == 0)
        {
            _keys!.Tap("IK_3");
            _camKeyDown = false;
        }
        else if (t >= 0.6 && !_camKeyDown)
        {
            _keys!.Tap("IK_RightMouse");
            _camKeyDown = true;
        }
    }

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_teardown >= 0)
        {
            return ++_teardown < TeardownFrames ? SmokeResult.Running : _final;
        }

        if (_root is null)
        {
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

        if (_keys is null)
        {
            _keys = KeyPilot.Attach(_root);
            _recorder = new SmoothRecorder { Root = _root };
            _root.AddChild(_recorder);
            if (_vsync == Vsync.Untouched && DisplayServer.GetName() != "headless")
            {
                _savedVsync = DisplayServer.WindowGetVsyncMode();
                _savedMaxFps = Engine.MaxFps;
                _vsync = Vsync.Saved;
                DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            }
        }

        _keys.Update();
        if (_phase < 0)
        {
            // The merc on his feet with the rifle selected, and the keys set up.
            bool ready = _keys.Ready && Pc is { } pc && pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal)
                && session.LocalPawn?.MyWeapon is { } w && w.GetStateName().ToString() == "s_Selected" && session.TicksRun > 120;
            if (!ready)
            {
                return session.TicksRun > 60 * 60 ? Finish(SmokeResult.Fail($"the merc never stood ready (state {Pc?.GetStateName()}, keys ready {_keys.Ready})")) : SmokeResult.Running;
            }

            _notes.AddRange(_keys.Notes);
            Enter(0, context);
        }

        Phase phase = _phases[_phase];
        double t = context.ElapsedSeconds - _phaseStart;
        bool done = t >= phase.MinSeconds && (phase.Done is null || phase.Done(this, t));
        if (done || t >= phase.MaxSeconds)
        {
            if (!done)
            {
                _notes.Add($"{phase.Label} at cap {phase.Cap} ran out after {phase.MaxSeconds:F1} s (state {Pc?.GetStateName()}, FOV {Pc?.FovAngle:F1})");
            }

            Shot($"{phase.Cap}-{phase.Label}");
            if (_phase + 1 >= _phases.Count)
            {
                return Finish(Analyse());
            }

            Enter(_phase + 1, context);
            phase = _phases[_phase];
            t = 0;
        }

        phase.Act?.Invoke(this, t);
        _phaseFrames++;
        return SmokeResult.Running;
    }

    private static PlayRoot Start(SmokeContext context)
    {
        var config = new PlayConfig
        {
            Settings = new PlaySettings
            {
                Map = "SteeE",
                Side = PlaySide.Merc,
                Rules = PlayRules.PlayLevel,
                HostMode = PlayHostMode.Thread,
                MapRoots = [.. context.App.Options.MapRoots],
                Bots = 0,
            },
            Render = true,
            Hud = true,
            TicksPerFrame = 0,
        };
        PlayRoot root = PlayRoot.Create(config);
        context.Tree.Root.AddChild(root);
        return root;
    }

    private void Enter(int index, SmokeContext context)
    {
        _phase = index;
        _phaseStart = context.ElapsedSeconds;
        _phaseFrames = 0;
        _recorder!.Phase = _phases[index].Label;
        _recorder.Cap = _phases[index].Cap;
        _recorder.Recording = true;
    }

    private void SetCap(int cap)
    {
        if (Engine.MaxFps != cap)
        {
            Engine.MaxFps = cap;
        }
    }

    private Vector2 Scale
    {
        get
        {
            Vector2 size = _root!.GetViewport().GetVisibleRect().Size;
            return new Vector2(size.X >= 1f ? KeyPilot.ReferenceWidth / size.X : 1f, size.Y >= 1f ? KeyPilot.ReferenceHeight / size.Y : 1f);
        }
    }

    private void LookSteady(float pixelsPerSecond)
    {
        double dt = _root!.GetProcessDeltaTime();
        _keys!.Look(pixelsPerSecond * (float)dt * Scale.X, 0f);
    }

    private int _impulsesSent;

    private void Impulse(double t)
    {
        if (_phaseFrames == 0)
        {
            _impulsesSent = 0;
        }

        // One move per gap, a few milliseconds later each time so the moves land at different points between ticks.
        double due = (_impulsesSent * ImpulseGap) + (0.004 * _impulsesSent);
        if (_impulsesSent < Impulses && t >= due)
        {
            _keys!.Look(ImpulsePixels * Scale.X, 0f);
            _impulsesSent++;
        }
    }

    private void TapOnce(string key)
    {
        if (_phaseFrames == 0)
        {
            _keys!.Tap(key);
        }
    }

    private SmokeResult Finish(SmokeResult result)
    {
        if (_recorder is not null)
        {
            _recorder.Recording = false;
        }

        _keys?.ReleaseAll();
        if (_vsync == Vsync.Saved)
        {
            DisplayServer.WindowSetVsyncMode(_savedVsync);
            Engine.MaxFps = _savedMaxFps;
        }

        _final = result;
        _teardown = 0;
        _root?.QueueFree();
        return SmokeResult.Running;
    }

    private void Shot(string step)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null)
        {
            return;
        }

        Directory.CreateDirectory(folder);

        // Reading the frame back stalls this frame; it is left out of the hitch list.
        _shotFrames.Add(_recorder?.Frames.Count ?? -1);
        Image image = _root.GetViewport().GetTexture().GetImage();
        string safe = string.Concat(step.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
        image.SavePng(Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{TestName}-{_shots++:D2}-{safe}.png")));
    }

    // ---- the checks --------------------------------------------------------------------------------------------

    private SmokeResult Analyse()
    {
        IReadOnlyList<SmoothFrame> frames = _recorder!.Frames;
        WriteCsv(frames);
        var lines = new List<string>();
        var failures = new List<string>();
        foreach (int cap in _caps)
        {
            SmoothFrame[] all = frames.Where(f => f.Cap == cap).ToArray();
            SmoothFrame[] settle = all.Where(f => f.Phase == "settle").ToArray();
            double fps = settle.Length > 2 ? 1000.0 / Median(settle.Skip(settle.Length / 2).Select(f => f.DtMs)) : 0;
            string c = cap == 0 ? "uncapped" : $"cap {cap}";
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"[{c}] {fps:F0} fps"));

            // A steady turn: every frame should move the camera, at a speed close to the median.
            SmoothFrame[] look = Middle(all.Where(f => f.Phase == "look").ToArray(), 0.2, 0.05);
            (int stalls, double ratio, int moved) = Motion(look, f => f.Rotation.Yaw);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  look: {look.Length} frames, camera still in {stalls}, fastest/median per-second speed {ratio:F2}"));
            if (stalls > 0 && moved > 0)
            {
                failures.Add($"{c} look: the drawn camera held still in {stalls} of {look.Length} frames of a steady mouse turn");
            }

            // A steady cam turn: every frame moves the camera at an even speed (no beat of two or three frames of
            // mouse a tick).
            SmoothFrame[] camLook = Middle(all.Where(f => f.Phase == "camnet look").ToArray(), 0.3, 0.05);
            (int camStalls, double camRatio, int camMoved) = Motion(camLook, f => f.Rotation.Yaw);
            double camSpread = Spread(camLook, f => f.Rotation.Yaw);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  camnet look: {camLook.Length} frames, camera still in {camStalls}, fastest/median {camRatio:F2}, p90/p10 {camSpread:F2}"));
            if (camMoved == 0)
            {
                failures.Add($"{c} camnet look: the camera network view did not turn ({camLook.Length} frames, state {camLook.LastOrDefault().State})");
            }
            else if (camStalls > 0)
            {
                failures.Add($"{c} camnet look: the drawn cam held still in {camStalls} of {camLook.Length} frames of a steady mouse turn");
            }
            else if (camSpread > CamLookSpreadLimit && DisplayServer.GetName() != "headless")
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{c} camnet look: the per-frame cam turn speed varies by {camSpread:F2}x (tick-rate beat)"));
            }

            // A single mouse move: drawn in full in the frame it reached the game.
            (List<int> late, List<double> lateMs) = Latency(all.Where(f => f.Phase == "impulse").ToArray());
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  mouse latency: frames after delivery to the full turn {string.Join(",", late)}; ms {string.Join(",", lateMs.Select(m => m.ToString("F1", CultureInfo.InvariantCulture)))}"));
            if (late.Count == 0)
            {
                failures.Add($"{c} mouse latency: no single mouse move was seen");
            }
            else if (late.Any(l => l > 0))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{c} mouse latency: a mouse move was drawn in full only {late.Max()} frame(s) ({lateMs.Max():F1} ms) after it reached the game"));
            }

            foreach (string zoom in new[] { "zoom in", "zoom out" })
            {
                SmoothFrame[] z = all.Where(f => f.Phase == zoom).ToArray();
                (int zStalls, int zFrames, float from, float to) = Ramp(z);
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {zoom}: FOV {from:F1} -> {to:F1}, ramp {zFrames} frames, still in {zStalls}"));
                if (zFrames == 0)
                {
                    failures.Add($"{c} {zoom}: the FOV did not change");
                }
                else if (zStalls > 0)
                {
                    failures.Add($"{c} {zoom}: the FOV held still in {zStalls} of {zFrames} frames of the zoom ramp");
                }
            }

            SmoothFrame[] scopeLook = Middle(all.Where(f => f.Phase == "scope look" && f.Sights is not null).ToArray(), 0.4, 0.05);
            float jitter = SightsJitter(scopeLook);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  scope sights: {scopeLook.Length} frames, saw tooth {jitter:F2} canvas units"));
            if (scopeLook.Length < 10)
            {
                failures.Add($"{c} scope sights: the sights were not drawn while turning in the scope ({scopeLook.Length} frames)");
            }
            else if (jitter > SightsJitterLimit && DisplayServer.GetName() == "headless")
            {
                // A 64 x 64 headless viewport scales the look 20 times and its frame times are uneven: window only.
                lines.Add("  (headless: the sights check needs a window)");
            }
            else if (jitter > SightsJitterLimit)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{c} scope sights: the per-frame sights motion jumps by {jitter:F2} canvas units (tick-rate saw tooth)"));
            }

            foreach (string clip in new[] { "scope in", "scope out" })
            {
                SmoothFrame[] v = all.Where(f => f.Phase == clip).ToArray();
                (int vStalls, int vFrames) = ClipStalls(v);
                lines.Add($"  view model {clip}: clip frames {vFrames}, still in {vStalls}");
                if (vFrames > 0 && vStalls > 0)
                {
                    failures.Add($"{c} view model {clip}: the sniping clip held still in {vStalls} of {vFrames} frames");
                }
            }

            int shots = all.Count(f => f.DtMs > HitchMs && _shotFrames.Contains(f.Index));
            if (shots > 0)
            {
                lines.Add($"  {shots} frame(s) over {HitchMs:F0} ms were screenshot saves (SVM_PLAY_SHOTS), left out");
            }

            SmoothFrame[] hitches = all.Where(f => f.DtMs > HitchMs && f.Phase != "settle" && !_shotFrames.Contains(f.Index)).ToArray();
            if (hitches.Length > MaxHitchLines)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {hitches.Length} frames over {HitchMs:F0} ms ({hitches.Count(h => h.GcPauseMs > 0)} with a garbage collection), the longest:"));
                hitches = hitches.OrderByDescending(h => h.DtMs).Take(MaxHitchLines).ToArray();
            }

            foreach (SmoothFrame h in hitches)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  hitch {h.DtMs:F1} ms in '{h.Phase}' (sim {h.SimMs:F1} ms, views {h.ViewMs:F1} ms, gc {h.Gc0}/{h.Gc1}/{h.Gc2} paused {h.GcPauseMs:F1} ms)"));
            }

            double gcFrames = all.Count(f => f.Gc0 + f.Gc1 + f.Gc2 > 0);
            double seconds = all.Length > 1 ? (all[^1].TimeMs - all[0].TimeMs) / 1000.0 : 0;
            double mbPerSecond = seconds > 0 ? all.Sum(f => f.Allocated) / seconds / (1024.0 * 1024.0) : 0;
            double worstPause = all.Length > 0 ? all.Max(f => f.GcPauseMs) : 0;
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  gc frames {gcFrames} (gen1 {all.Sum(f => f.Gc1)}, gen2 {all.Sum(f => f.Gc2)}), longest pause {worstPause:F1} ms, allocated {mbPerSecond:F1} MB/s, worst frame {(all.Length > 0 ? all.Max(f => f.DtMs) : 0):F1} ms"));
        }

        foreach (string line in lines)
        {
            GD.Print("SVM_SMOOTH " + line);
        }

        foreach (string note in _notes)
        {
            GD.Print("SVM_SMOOTH note " + note);
        }

        return failures.Count == 0
            ? SmokeResult.Pass(string.Join(" | ", lines))
            : SmokeResult.Fail(string.Join(" || ", failures));
    }

    private static SmoothFrame[] Middle(SmoothFrame[] frames, double skipStart, double skipEnd)
    {
        if (frames.Length == 0)
        {
            return frames;
        }

        double t0 = frames[0].TimeMs + (skipStart * 1000.0), t1 = frames[^1].TimeMs - (skipEnd * 1000.0);
        return frames.Where(f => f.TimeMs >= t0 && f.TimeMs <= t1).ToArray();
    }

    /// <summary>The 90th over the 10th percentile of the per-second change of <paramref name="axis"/>.</summary>
    private static double Spread(SmoothFrame[] frames, Func<SmoothFrame, int> axis)
    {
        var speeds = new List<double>();
        for (int i = 1; i < frames.Length; i++)
        {
            speeds.Add(Math.Abs(FRotator.NormalizeAxis(axis(frames[i]) - axis(frames[i - 1]))) / Math.Max(0.001, frames[i].DtMs));
        }

        if (speeds.Count < 4)
        {
            return 0;
        }

        speeds.Sort();
        double low = speeds[speeds.Count / 10];
        return low > 0 ? speeds[speeds.Count * 9 / 10] / low : double.PositiveInfinity;
    }

    private static double Median(IEnumerable<double> values)
    {
        double[] v = values.OrderBy(x => x).ToArray();
        return v.Length == 0 ? 0 : v[v.Length / 2];
    }

    /// <summary>Frames whose value did not change, and the fastest per-second change over the median.</summary>
    private static (int Stalls, double Ratio, int Moved) Motion(SmoothFrame[] frames, Func<SmoothFrame, int> axis)
    {
        int stalls = 0, moved = 0;
        var speeds = new List<double>();
        for (int i = 1; i < frames.Length; i++)
        {
            int d = FRotator.NormalizeAxis(axis(frames[i]) - axis(frames[i - 1]));
            if (d == 0)
            {
                stalls++;
            }
            else
            {
                moved++;
            }

            speeds.Add(Math.Abs(d) / Math.Max(0.001, frames[i].DtMs));
        }

        double median = Median(speeds);
        return (stalls, median > 0 ? speeds.Max() / median : 0, moved);
    }

    /// <summary>
    /// For each single mouse move: the frames from the one it reached the game in to the first that draws 99 percent of
    /// the turn it makes, and that time in milliseconds.
    /// </summary>
    private static (List<int> Frames, List<double> Ms) Latency(SmoothFrame[] frames)
    {
        var late = new List<int>();
        var ms = new List<double>();
        for (int i = 1; i < frames.Length; i++)
        {
            if (frames[i].Motions == 0)
            {
                continue;
            }

            int y0 = frames[i - 1].Rotation.Yaw;
            int end = i;
            while (end + 1 < frames.Length && frames[end + 1].Motions == 0 && frames[end + 1].TimeMs - frames[i].TimeMs < 200.0)
            {
                end++;
            }

            int total = FRotator.NormalizeAxis(frames[end].Rotation.Yaw - y0);
            if (Math.Abs(total) < 10)
            {
                continue;
            }

            for (int k = i; k <= end; k++)
            {
                if (Math.Abs(FRotator.NormalizeAxis(frames[k].Rotation.Yaw - y0)) >= 0.99 * Math.Abs(total))
                {
                    late.Add(k - i);
                    ms.Add(frames[k].TimeMs - frames[i].TimeMs);
                    break;
                }
            }
        }

        return (late, ms);
    }

    /// <summary>The zoom ramp: frames from the first FOV change to the last, and the frames inside it that held still.</summary>
    private static (int Stalls, int Frames, float From, float To) Ramp(SmoothFrame[] frames)
    {
        if (frames.Length < 3)
        {
            return (0, 0, 0f, 0f);
        }

        int first = -1, last = -1;
        for (int i = 1; i < frames.Length; i++)
        {
            if (frames[i].Fov != frames[i - 1].Fov)
            {
                first = first < 0 ? i : first;
                last = i;
            }
        }

        if (first < 0)
        {
            return (0, 0, frames[0].Fov, frames[^1].Fov);
        }

        int stalls = 0;
        for (int i = first; i <= last; i++)
        {
            if (frames[i].Fov == frames[i - 1].Fov)
            {
                stalls++;
            }
        }

        return (stalls, last - first + 1, frames[first - 1].Fov, frames[last].Fov);
    }

    /// <summary>The largest change of the per-frame sights motion (a saw tooth when the aim point steps at the tick rate).</summary>
    private static float SightsJitter(SmoothFrame[] frames)
    {
        float worst = 0f;
        for (int i = 2; i < frames.Length; i++)
        {
            Vector2 a = frames[i - 2].Sights!.Value, b = frames[i - 1].Sights!.Value, c = frames[i].Sights!.Value;
            Vector2 d1 = b - a, d2 = c - b;

            // The motion per millisecond, so uneven frame times are not counted as jitter; back to this frame's length.
            float k1 = (float)frames[i - 1].DtMs, k2 = (float)frames[i].DtMs;
            if (k1 <= 0f || k2 <= 0f)
            {
                continue;
            }

            Vector2 diff = (d2 / k2 - d1 / k1) * k2;
            worst = Math.Max(worst, diff.Length());
        }

        return worst;
    }

    /// <summary>Frames the sniping clip played in (strictly between its ends), and those where it did not move.</summary>
    private static (int Stalls, int Frames) ClipStalls(SmoothFrame[] frames)
    {
        int stalls = 0, count = 0;
        for (int i = 1; i < frames.Length; i++)
        {
            float a = frames[i - 1].ScopeChannel, b = frames[i].ScopeChannel;
            if (a <= 0f || b <= 0f || a >= 0.999f || b >= 0.999f)
            {
                continue;
            }

            count++;
            if (a == b)
            {
                stalls++;
            }
        }

        return (stalls, count);
    }

    private static void WriteCsv(IReadOnlyList<SmoothFrame> frames)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_SMOOTH_CSV");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var sb = new StringBuilder("index,cap,phase,time_ms,dt_ms,ticks,yaw,pitch,fov,sim_yaw,sim_fov,state,sights_x,sights_y,scope_channel,motions,motion_dx,gc0,gc1,gc2,sim_ms,view_ms,gc_pause_ms,allocated\n");
        foreach (SmoothFrame f in frames)
        {
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $"{f.Index},{f.Cap},{f.Phase},{f.TimeMs:F3},{f.DtMs:F3},{f.Ticks},{f.Rotation.Yaw},{f.Rotation.Pitch},{f.Fov:F4},{f.SimRotation.Yaw},{f.SimFov:F4},{f.State},{f.Sights?.X:F3},{f.Sights?.Y:F3},{f.ScopeChannel:F4},{f.Motions},{f.MotionDx:F2},{f.Gc0},{f.Gc1},{f.Gc2},{f.SimMs:F2},{f.ViewMs:F2},{f.GcPauseMs:F2},{f.Allocated}\n"));
        }

        File.WriteAllText(Path.Combine(folder, "smooth-frames.csv"), sb.ToString());
    }
}
