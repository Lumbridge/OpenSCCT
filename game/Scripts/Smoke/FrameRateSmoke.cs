using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScctGame.Options;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=frame-rate</c>: the player's frame rate settings and what they see at each (docs/RENDERING.md, frame
/// pacing). The spy plays SteeE as the launchers run it (wall clock, listen server on its own thread, HUD). For each
/// case of SVM_FRAMERATE_CASES (default <c>monitor,60,144,165,240,unlimited,monitor-vsync</c>; a number is a cap,
/// <c>-vsync</c> turns vertical sync on) the settings are saved and applied as the options screen does
/// (<see cref="SettingsStore.Save"/>, <see cref="SettingsApplier.ApplyPacing"/>, the play scene's
/// <see cref="OptionsRuntime"/> choosing the side's cap every frame), <see cref="KeyPilot"/> walks the spy forward
/// or back and then turns the mouse steadily, and every frame the drawn camera, the spy's drawn pawn, the scene time
/// of the map (materials, lights), its emitters and the HUD's effects are read after every view has drawn. Checks:
/// <list type="bullet">
/// <item>the frame rate is the one the settings ask for (the monitor's refresh rate by default);</item>
/// <item>each of those moves in every frame (nothing steps at the 60 Hz tick rate) and evenly: the step jitter
/// (<see cref="FrameMotion.Motion"/>) stays under <see cref="JitterLimit"/>.</item>
/// </list>
/// SVM_FRAMERATE_CSV names a folder under local/ for the per-frame values; with a window and SVM_PLAY_SHOTS the frame
/// at the end of each case is saved. Run it with a window (headless has no frame cap or refresh rate).
/// </summary>
public sealed class FrameRateSmoke : SmokeTest
{
    public const string TestName = "frame-rate";

    /// <summary>The largest step jitter allowed (share of the mean per-frame step).</summary>
    public const double JitterLimit = 0.35;

    /// <summary>The largest 90th over 10th percentile of the per-frame speed of a steady mouse turn (as the smooth smoke's cam turn).</summary>
    public const double SpreadLimit = 1.3;

    /// <summary>
    /// The frame times are even enough (90th over 10th percentile) for the turn speed's spread to show a tick-rate beat;
    /// above it (uncapped) the spread is the frames' own and the step jitter is checked instead.
    /// </summary>
    public const double PacedSpread = 1.1;

    /// <summary>A frame this many median frames long is a hitch (listed as slow; its motion checks skip it).</summary>
    private const double HitchFactor = 3.0;

    /// <summary>The simulated spy counts as walking above this speed (uu/s).</summary>
    private const float MovingSpeed = 5f;

    /// <summary>How far the measured frame rate may be from the one asked for.</summary>
    public const double RateTolerance = 0.06;

    private const double SettleSeconds = 1.0;
    private const double WalkSeconds = 1.8;
    private const double TurnSeconds = 1.0;
    private const double SkipSeconds = 0.4;
    private const double WalkProbeSeconds = 0.5;
    private static readonly string[] Walks = ["BT_Up", "BT_Down", "BT_Left", "BT_Right"];
    private const float TurnPixelsPerSecond = 400f;
    private const int TeardownFrames = 4;

    private readonly List<Case> _cases = new();
    private readonly List<string> _notes = new();
    private readonly List<string> _failures = new();
    private readonly List<string> _lines = new();
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private FrameRateRecorder? _recorder;
    private int _case = -1;
    private int _step;
    private double _stepStart;
    private int _stepFrames;
    private int _walk;
    private int _walkTries;
    private FVector _walkFrom;
    private bool _entered;
    private bool _paced;
    private bool _optionsShown;
    private int _shots;
    private int _teardown = -1;
    private SmokeResult _final;
    private bool _saved;
    private DisplayServer.VSyncMode _savedVsync;
    private int _savedMaxFps;

    public FrameRateSmoke()
    {
        string? text = System.Environment.GetEnvironmentVariable("SVM_FRAMERATE_CASES");
        string[] names = string.IsNullOrWhiteSpace(text)
            ? ["monitor", "60", "144", "165", "240", "unlimited", "monitor-vsync", "monitor-move"]
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string name in names)
        {
            bool vsync = name.EndsWith("-vsync", StringComparison.OrdinalIgnoreCase);
            bool move = name.EndsWith("-move", StringComparison.OrdinalIgnoreCase);
            string cap = vsync ? name[..^6] : move ? name[..^5] : name;
            int value = cap.ToLowerInvariant() switch
            {
                "monitor" => DisplayFrameCap.Monitor,
                "unlimited" or "0" => DisplayFrameCap.Unlimited,
                _ => DisplayFrameCap.Clamp(int.Parse(cap, CultureInfo.InvariantCulture)),
            };
            _cases.Add(new Case(name, value, vsync, move));
        }
    }

    public override string Name => TestName;

    /// <summary>A case: the cap and vertical sync, and whether the window moves to the next screen first (the cap follows it).</summary>
    private sealed record Case(string Label, int Cap, bool VSync, bool Move = false);

    /// <summary>What a case asked for once its settings took: the screen's rate, the frame rate and max_fps.</summary>
    private readonly record struct Asked(double Hz, int Fps, int MaxFps);

    private readonly Dictionary<string, Asked> _asked = new();
    private int _homeScreen = -1;

    private enum Phase
    {
        Settle,
        Walk,
        Turn,
        Options,
    }

    private SPlayerController? Pc => _root?.Session?.LocalController;

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
            if (DisplayServer.GetName() == "headless")
            {
                return Finish(SmokeResult.Fail("frame-rate needs a window: headless has no frame cap or refresh rate"));
            }

            _keys = KeyPilot.Attach(_root);
            _recorder = new FrameRateRecorder { Root = _root };
            _root.AddChild(new FrameStartProbe { Recorder = _recorder });
            _root.AddChild(_recorder);
            _savedVsync = DisplayServer.WindowGetVsyncMode();
            _savedMaxFps = Engine.MaxFps;
            _saved = true;

            // The play scene's settings runtime chooses the side's cap every frame as in a player's game (smoke runs
            // leave it off), and the pacer follows the window's screen.
            if (_root.GetNodeOrNull<OptionsRuntime>("OptionsRuntime") is { } runtime)
            {
                runtime.CapFrames = true;
            }
            else
            {
                _notes.Add("no OptionsRuntime in the play scene: the cap is applied by the smoke only");
            }

            _root.AddChild(new DisplayFramePacer());
        }

        _keys.Update();
        if (_case < 0)
        {
            bool ready = _keys.Ready && Pc is { } pc && pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal)
                && session.LocalPawn is not null && session.TicksRun > 120;
            if (!ready)
            {
                return session.TicksRun > 60 * 60
                    ? Finish(SmokeResult.Fail($"the spy never stood ready (state {Pc?.GetStateName()}, keys ready {_keys.Ready})"))
                    : SmokeResult.Running;
            }

            _notes.AddRange(_keys.Notes);
            _notes.Add(string.Create(CultureInfo.InvariantCulture,
                $"screen {SettingsApplier.MonitorScreen + 1} of {DisplayServer.GetScreenCount()} at {SettingsApplier.MonitorHz:F2} Hz, window {DisplayServer.WindowGetSize()}"));
            Enter(0, Phase.Settle, context);
        }

        Case c = _cases[_case];
        double t = context.ElapsedSeconds - _stepStart;
        switch ((Phase)_step)
        {
            case Phase.Settle:
                if (_stepFrames == 0)
                {
                    Apply(c);
                }

                if (t >= SettleSeconds)
                {
                    CheckApplied(c);
                    Enter(_case, Phase.Walk, context);
                }

                break;
            case Phase.Walk:
                // Forward, back, left or right: the first way the spy can walk (a wall may be in front of him).
                string walk = Walks[_walk % Walks.Length];
                if (_stepFrames == 0)
                {
                    _keys.Press(walk);
                    _walkFrom = session.LocalPawn?.Location ?? default;
                }

                if (t >= WalkProbeSeconds && _walkTries < Walks.Length && session.LocalPawn is { } walker && (walker.Location - _walkFrom).Size2D() < 15f)
                {
                    _keys.Release(walk);
                    _notes.Add($"{c.Label}: the spy did not walk with {walk} ({(walker.Location - _walkFrom).Size2D():F1} uu); trying the next way");
                    _walk++;
                    _walkTries++;
                    _recorder!.Drop(c.Label, "walk");
                    Enter(_case, Phase.Walk, context);
                    break;
                }

                if (t >= WalkSeconds)
                {
                    _keys.Release(walk);
                    _walk++;
                    _walkTries = 0;
                    Enter(_case, Phase.Turn, context);
                }

                break;
            case Phase.Options:
                // The options over the game (F12, as a player opens them) on the Display page.
                if (_stepFrames == 0)
                {
                    _keys.Tap("IK_F12");
                }
                else if (t >= 0.6 && !_optionsShown)
                {
                    _optionsShown = true;
                    CheckOptions();
                }
                else if (t >= 1.0)
                {
                    Shot("options-display");
                    return Finish(Analyse());
                }

                break;
            default:
                if (t >= TurnSeconds)
                {
                    // The frame is read back after the measured frames (reading it stalls the game, and the session then
                    // drops the ticks it fell behind by).
                    _recorder!.Recording = false;
                    Shot($"{c.Label}-turn");
                    if (_case + 1 >= _cases.Count)
                    {
                        Enter(_case, Phase.Options, context);
                        _recorder.Recording = false;
                        break;
                    }

                    Enter(_case + 1, Phase.Settle, context);
                }
                else
                {
                    double dt = _root.GetProcessDeltaTime();
                    Vector2 size = _root.GetViewport().GetVisibleRect().Size;
                    float scale = size.X >= 1f ? KeyPilot.ReferenceWidth / size.X : 1f;
                    _keys.Look(TurnPixelsPerSecond * (float)dt * scale, 0f);
                }

                break;
        }

        // A step entered in this frame starts counting its frames in the next one (its first actions run then).
        _stepFrames = _entered ? 0 : _stepFrames + 1;
        _entered = false;
        return SmokeResult.Running;
    }

    private static PlayRoot Start(SmokeContext context)
    {
        var config = new PlayConfig
        {
            Settings = new PlaySettings
            {
                Map = "SteeE",
                Side = PlaySide.Spy,
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

    private void Enter(int index, Phase step, SmokeContext context)
    {
        _case = index;
        _step = (int)step;
        _stepStart = context.ElapsedSeconds;
        _stepFrames = 0;
        _entered = true;
        _recorder!.Case = _cases[index].Label;
        _recorder.Step = step.ToString().ToLowerInvariant();
        _recorder.Recording = true;
    }

    /// <summary>The case's settings, saved and applied as the options screen's Save does.</summary>
    private void Apply(Case c)
    {
        GameSettings s = SettingsStore.Current.Clone();
        s.FpsSpy = c.Cap;
        s.FpsMerc = c.Cap;
        s.VSync = c.VSync;
        SettingsStore.Save(s);
        SettingsApplier.ApplyPacing(s);

        // The window goes to the next screen; the pacer finds it there and applies that screen's rate.
        if (c.Move && _homeScreen < 0)
        {
            int count = DisplayServer.GetScreenCount();
            if (count < 2)
            {
                _notes.Add($"{c.Label}: one screen only, the window stays");
                return;
            }

            _homeScreen = DisplayServer.WindowGetCurrentScreen();
            DisplayServer.WindowSetCurrentScreen((_homeScreen + 1) % count);
        }
    }

    private void CheckApplied(Case c)
    {
        int screen = DisplayServer.WindowGetCurrentScreen();
        double hz = DisplayServer.ScreenGetRefreshRate(screen);
        int want = DisplayFrameCap.EngineMaxFps(c.Cap, hz, c.VSync);
        _asked[c.Label] = new Asked(hz, DisplayFrameCap.ExpectedFps(c.Cap, hz, c.VSync), want);
        if (c.Move)
        {
            _notes.Add(string.Create(CultureInfo.InvariantCulture,
                $"{c.Label}: window on screen {screen + 1} ({hz:F2} Hz), the pacer read screen {SettingsApplier.MonitorScreen + 1} at {SettingsApplier.MonitorHz:F2} Hz"));
            if (SettingsApplier.MonitorScreen != screen)
            {
                _failures.Add($"{c.Label}: the frame cap did not follow the window to screen {screen + 1}");
            }
        }

        DisplayServer.VSyncMode vsync = DisplayServer.WindowGetVsyncMode();
        bool vsyncOn = vsync != DisplayServer.VSyncMode.Disabled;
        if (Engine.MaxFps != want || vsyncOn != c.VSync)
        {
            _failures.Add($"{c.Label}: the settings did not take: max_fps {Engine.MaxFps} (want {want}), vsync {vsync}");
        }
    }

    /// <summary>The F12 options show the max frame rate rows with the monitor's rate and vertical sync on the Display page.</summary>
    private void CheckOptions()
    {
        OptionsMenu? menu = Find<OptionsMenu>(_root!);
        if (menu?.Tabs is not { } tabs)
        {
            _failures.Add("options: F12 did not open the options over the game");
            return;
        }

        for (int i = 0; i < tabs.GetTabCount(); i++)
        {
            if (tabs.GetTabTitle(i) == "Display")
            {
                tabs.CurrentTab = i;
            }
        }

        foreach (string key in new[] { "fps_spy", "fps_merc" })
        {
            string shown = menu.Controls.TryGetValue(key, out Control? control) && control is OptionButton option && option.Selected >= 0
                ? option.GetItemText(option.Selected)
                : "";
            string want = DisplayFrameCap.Label(SettingsStore.Current.FpsFor(key == "fps_merc"), SettingsApplier.MonitorHz);
            _notes.Add($"options: {key} shows '{shown}'");
            if (shown != want)
            {
                _failures.Add($"options: {key} shows '{shown}', the settings hold '{want}'");
            }
        }

        if (!menu.Controls.ContainsKey("vsync"))
        {
            _failures.Add("options: no vertical sync row");
        }
    }

    private static T? Find<T>(Node node)
        where T : Node
    {
        foreach (Node child in node.GetChildren())
        {
            if (child is T found)
            {
                return found;
            }

            if (Find<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        if (_recorder is not null)
        {
            _recorder.Recording = false;
        }

        _keys?.ReleaseAll();
        if (_homeScreen >= 0)
        {
            DisplayServer.WindowSetCurrentScreen(_homeScreen);
            _homeScreen = -1;
        }

        if (_saved)
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
        if (string.IsNullOrWhiteSpace(folder) || _root is null)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        _recorder?.MarkShot();
        Image image = _root.GetViewport().GetTexture().GetImage();
        string safe = string.Concat(step.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
        image.SavePng(Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{TestName}-{_shots++:D2}-{safe}.png")));
    }

    // ---- the checks --------------------------------------------------------------------------------------------

    private SmokeResult Analyse()
    {
        IReadOnlyList<RateFrame> frames = _recorder!.Frames;
        WriteCsv(frames);

        // What the machine draws with no limit: a cap above it cannot be reached.
        double most = _cases.Where(c => c.Cap == DisplayFrameCap.Unlimited && !c.VSync)
            .Select(c => FrameMotion.Times(Middle(frames.Where(f => f.Case == c.Label && f.Step == "walk").ToArray()).Select(f => f.StartMs).ToArray()).Fps)
            .DefaultIfEmpty(0).Max();
        foreach (Case c in _cases)
        {
            RateFrame[] walk = Middle(frames.Where(f => f.Case == c.Label && f.Step == "walk").ToArray());
            RateFrame[] turn = Middle(frames.Where(f => f.Case == c.Label && f.Step == "turn").ToArray());
            // The walk and the turn together, without a gap (the times move through both).
            RateFrame[] both = Middle(frames.Where(f => f.Case == c.Label && f.Step is "walk" or "turn").ToArray());
            FrameTimeStats times = FrameMotion.Times(walk.Select(f => f.StartMs).ToArray());
            Asked asked = _asked.GetValueOrDefault(c.Label);
            int expected = asked.Fps;
            _lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"[{c.Label}] asks {(expected == 0 ? "no limit" : expected + " fps")}, screen {asked.Hz:F2} Hz, max_fps {asked.MaxFps}: {times.Fps:F1} fps, frame {times.MedianMs:F2} ms (p1 {times.P1Ms:F2}, p99 {times.P99Ms:F2}, sd {times.StdDevMs:F2}, p90/p10 {times.Spread:F2}), {times.Slow} slow"));
            // Vertical sync holds the rate at most at the screen's (a machine that cannot keep up shows fewer frames);
            // the limiter holds it at the cap.
            bool tooFast = expected > 0 && times.Fps > expected * (1 + RateTolerance);
            bool reachable = most <= 0 || most >= expected * (1 - RateTolerance);
            bool tooSlow = expected > 0 && !c.VSync && reachable && times.Fps < expected * (1 - RateTolerance);
            if (expected > 0 && !reachable)
            {
                _notes.Add(string.Create(CultureInfo.InvariantCulture, $"{c.Label}: the machine draws {most:F0} fps with no limit, under the {expected} asked"));
            }
            if (tooFast || tooSlow)
            {
                _failures.Add(string.Create(CultureInfo.InvariantCulture, $"{c.Label}: {times.Fps:F1} fps where the settings ask {expected}"));
            }

            _paced = times.Spread <= PacedSpread;

            // The camera's own motion while walking is the solver's (it chases, collides and pulls in): reported only.
            Check(c, "camera (walk)", walk, Along(walk, f => f.Camera), 0.001, Rule.Report);
            // Frames where the simulated spy stood still (against a wall: UE2 walking sets Velocity from the move made) are
            // left out of the pawn's check.
            RateFrame[] moving = walk.Where(f => f.SimSpeed > MovingSpeed).ToArray();
            Check(c, "spy pawn (walk)", moving, Along(moving, f => f.Pawn), 0.001, Rule.Steps);
            Check(c, "camera yaw (turn)", turn, turn.Select(f => f.Yaw).ToArray(), 0.0, Rule.Even);
            Check(c, "map time", both, both.Select(f => f.MapTimeMs).ToArray(), 0.0, Rule.Steady);
            Check(c, "emitters", both, both.Select(f => f.EmitterTimeMs).ToArray(), 0.0, Rule.Steady, optional: true);
            Check(c, "HUD effects time", both, both.Select(f => f.HudTimeMs).ToArray(), 0.0, Rule.Steady);
        }

        foreach (string line in _lines)
        {
            GD.Print("SVM_FRAMERATE " + line);
        }

        foreach (string note in _notes)
        {
            GD.Print("SVM_FRAMERATE note " + note);
        }

        return _failures.Count == 0 ? SmokeResult.Pass(string.Join(" | ", _lines.Where(l => l.StartsWith('[')))) : SmokeResult.Fail(string.Join(" || ", _failures));
    }

    /// <summary>The distance of each frame's point from the first along the line to the last (uu).</summary>
    private static double[] Along(RateFrame[] frames, Func<RateFrame, FVector> point)
    {
        if (frames.Length < 2)
        {
            return new double[frames.Length];
        }

        FVector origin = point(frames[0]);
        FVector direction = (point(frames[^1]) - origin).SafeNormal();
        return frames.Select(f => (double)((point(f) - origin) | direction)).ToArray();
    }

    /// <summary>What a measure must do: be reported, not step, turn at an even speed, or move every frame steadily.</summary>
    private enum Rule
    {
        Report,
        Steps,
        Even,
        Steady,
    }

    private void Check(Case c, string what, RateFrame[] frames, double[] values, double still, Rule rule, bool optional = false)
    {
        // A screenshot frame stalls the next one; those two are left out. So are a hitch (a frame over
        // HitchFactor medians: the mouse of its whole length reaches the next frames at once) and the two frames after it;
        // the steps across them still cover the time and the motion in between.
        double[] gaps = Enumerable.Range(1, Math.Max(0, frames.Length - 1)).Select(i => frames[i].StartMs - frames[i - 1].StartMs).OrderBy(g => g).ToArray();
        double median = gaps.Length > 0 ? gaps[gaps.Length / 2] : 0;
        var skip = new HashSet<int>();
        for (int i = 1; i < frames.Length; i++)
        {
            if (median > 0 && frames[i].StartMs - frames[i - 1].StartMs > HitchFactor * median)
            {
                skip.Add(i);
                skip.Add(i + 1);
                skip.Add(i + 2);
            }
        }

        int[] kept = Enumerable.Range(0, frames.Length).Where(i => !frames[i].AfterShot && !skip.Contains(i)).ToArray();
        // A value stepping at the tick rate holds still for up to a tick's frames at a time.
        double[] times = kept.Select(i => frames[i].StartMs).ToArray();
        int tickFrames = (int)Math.Ceiling(FrameMotion.Times(times).Fps / OpenScct.Core.Sim.Hz) + 1;
        MotionStats m = FrameMotion.Motion(times, kept.Select(i => values[i]).ToArray(), still, Math.Max(FrameMotion.StepRun, tickFrames));
        _lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"  {what}: {m.Frames} frames, still in {m.Still} ({m.Steps} between moving frames), step {m.MeanStep:F3}, speed p90/p10 {m.Spread:F2}, step jitter {m.Jitter:F3}"));
        if (rule == Rule.Report)
        {
            return;
        }

        if (!m.Moved)
        {
            if (!optional)
            {
                _failures.Add($"{c.Label} {what}: did not move ({m.Frames} frames)");
            }

            return;
        }

        if (rule == Rule.Steps ? m.Steps > 0 : m.Still > 0)
        {
            _failures.Add($"{c.Label} {what}: held still in {(rule == Rule.Steps ? m.Steps : m.Still)} of {m.Frames - 1} frames (steps at the tick rate)");
        }
        else if (rule == Rule.Even && _paced && m.Spread > SpreadLimit)
        {
            _failures.Add(string.Create(CultureInfo.InvariantCulture, $"{c.Label} {what}: the per-frame speed varies by {m.Spread:F2}x (over {SpreadLimit:F2}: a tick-rate beat)"));
        }
        else if ((rule == Rule.Steady || (rule == Rule.Even && !_paced)) && m.Jitter > JitterLimit)
        {
            _failures.Add(string.Create(CultureInfo.InvariantCulture, $"{c.Label} {what}: step jitter {m.Jitter:F2} (over {JitterLimit:F2})"));
        }
    }

    /// <summary>A step's frames without its first <see cref="SkipSeconds"/> (the key and the cap taking hold).</summary>
    private static RateFrame[] Middle(RateFrame[] frames)
    {
        if (frames.Length == 0)
        {
            return frames;
        }

        double t0 = frames[0].StartMs + (SkipSeconds * 1000.0);
        return frames.Where(f => f.StartMs >= t0).ToArray();
    }

    private static void WriteCsv(IReadOnlyList<RateFrame> frames)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_FRAMERATE_CSV");
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        var sb = new StringBuilder("case,step,start_ms,end_ms,ticks,alpha,camera_x,camera_y,camera_z,pawn_x,pawn_y,pawn_z,yaw,map_time_ms,emitter_time_ms,hud_time_ms,max_fps,after_shot\n");
        foreach (RateFrame f in frames)
        {
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $"{f.Case},{f.Step},{f.StartMs:F3},{f.EndMs:F3},{f.Ticks},{f.Alpha:F4},{f.Camera.X:F3},{f.Camera.Y:F3},{f.Camera.Z:F3},{f.Pawn.X:F3},{f.Pawn.Y:F3},{f.Pawn.Z:F3},{f.Yaw:F1},{f.MapTimeMs:F3},{f.EmitterTimeMs:F3},{f.HudTimeMs:F3},{f.MaxFps},{(f.AfterShot ? 1 : 0)}\n"));
        }

        File.WriteAllText(Path.Combine(folder, "frame-rate.csv"), sb.ToString());
    }
}

/// <summary>One frame of the frame-rate smoke: when it started and what was drawn.</summary>
public readonly record struct RateFrame
{
    public string Case { get; init; }

    public string Step { get; init; }

    /// <summary>Wall clock at the start of the frame's process pass (the first node to run), in milliseconds.</summary>
    public double StartMs { get; init; }

    /// <summary>Wall clock after every view has drawn, in milliseconds.</summary>
    public double EndMs { get; init; }

    public long Ticks { get; init; }

    public float Alpha { get; init; }

    /// <summary>The drawn camera and spy pawn locations (uu).</summary>
    public FVector Camera { get; init; }

    public FVector Pawn { get; init; }

    /// <summary>The simulated spy's horizontal speed after the last tick (uu/s).</summary>
    public float SimSpeed { get; init; }

    /// <summary>The drawn camera yaw, unwrapped (rotation units).</summary>
    public double Yaw { get; init; }

    /// <summary>The scene time of the map, its emitters and the HUD's effects, in milliseconds.</summary>
    public double MapTimeMs { get; init; }

    public double EmitterTimeMs { get; init; }

    public double HudTimeMs { get; init; }

    public int MaxFps { get; init; }

    /// <summary>The frame after a screenshot was read back (left out of the checks).</summary>
    public bool AfterShot { get; init; }
}

/// <summary>The first node of every frame's process pass: notes when the frame started.</summary>
public partial class FrameStartProbe : Node
{
    public FrameStartProbe()
    {
        Name = "FrameStartProbe";
        ProcessPriority = int.MinValue;
        ProcessMode = ProcessModeEnum.Always;
    }

    public FrameRateRecorder? Recorder { get; set; }

    public override void _Process(double delta)
    {
        if (Recorder is not null)
        {
            Recorder.FrameStartUsec = Time.GetTicksUsec();
        }
    }
}

/// <summary>Reads what was drawn after every view and the HUD have drawn (the last node of the process pass). It only reads.</summary>
public partial class FrameRateRecorder : Node
{
    private readonly List<RateFrame> _frames = new();
    private double _yaw;
    private int _lastYaw;
    private bool _hasYaw;
    private int _shotFrames;

    public FrameRateRecorder()
    {
        Name = "FrameRateRecorder";
        ProcessPriority = int.MaxValue;
        ProcessMode = ProcessModeEnum.Always;
    }

    public PlayRoot? Root { get; set; }

    public string Case { get; set; } = "";

    public string Step { get; set; } = "";

    public bool Recording { get; set; }

    public ulong FrameStartUsec { get; set; }

    public IReadOnlyList<RateFrame> Frames => _frames;

    /// <summary>Forgets the frames of a step (a walk tried again another way).</summary>
    public void Drop(string forCase, string step) => _frames.RemoveAll(f => f.Case == forCase && f.Step == step);

    /// <summary>The next two frames follow a screenshot read back.</summary>
    public void MarkShot() => _shotFrames = 2;

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        if (!Recording || Root is not { Session: { } session, Camera: { } camera })
        {
            return;
        }

        (FVector location, FRotator rotation, float _) = camera.Current;
        SBasePawn? pawn = session.LocalPawn;
        FVector pawnAt = Root.Pawns?.ViewOf(pawn)?.Model is { } model ? UeSpace.ToUe(model.Root.GlobalPosition) : pawn?.Location ?? default;

        int yaw = rotation.Yaw;
        _yaw += _hasYaw ? FRotator.NormalizeAxis(yaw - _lastYaw) : 0;
        _lastYaw = yaw;
        _hasYaw = true;
        _frames.Add(new RateFrame
        {
            Case = Case,
            Step = Step,
            StartMs = FrameStartUsec / 1000.0,
            EndMs = now / 1000.0,
            Ticks = session.TicksRun,
            Alpha = session.Alpha,
            Camera = location,
            Pawn = pawnAt,
            SimSpeed = pawn?.Velocity.Size2D() ?? 0f,
            Yaw = _yaw,
            MapTimeMs = (Root.Map?.Time ?? 0f) * 1000.0,
            EmitterTimeMs = (Root.Map?.Emitters?.SimulatedTime ?? 0f) * 1000.0,
            HudTimeMs = (Root.Hud?.EffectsTime ?? 0f) * 1000.0,
            MaxFps = Engine.MaxFps,
            AfterShot = _shotFrames > 0,
        });
        if (_shotFrames > 0)
        {
            _shotFrames--;
        }
    }

}
