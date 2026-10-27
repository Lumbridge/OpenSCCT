using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-anim</c>: the clips the player sees, through the real input path (docs/TESTING-PLAY.md). SteeE
/// offline as the launchers play it (Play Level rules, the listen server on its own thread, wall clock, bots), the
/// side from <c>SVM_ANIM_SIDE</c> (spy, merc; default spy) and the bots from <c>SVM_ANIM_BOTS</c> (default 3).
/// <see cref="KeyPilot"/> plays the moveset with the bound keys: idle, walk, run, backwards, sideways, crouch,
/// crouch walk, stand, jump, roll, draw and fire. Each frame it reads every drawn pawn's channels on the client and
/// records, per step and channel, the sequences played, whether the mesh has them and whether the pose took them,
/// and how far the drawn bones moved, and the merc's first-person view model channels. It fails when the local pawn
/// moves while its drawn pose does not, or when the local pawn or the view model draws a clip the mesh or the
/// animation set lacks; the same findings for the bots are listed as notes. The clip table goes to
/// <c>local/anim-play/&lt;side&gt;.md</c> (docs/ANIMATION.md, gap table).
/// </summary>
public sealed class PlayAnimSmoke : SmokeTest
{
    public const string TestName = "play-anim";

    private const int TeardownFrames = 4;

    /// <summary>A pawn counts as moving above this speed (uu/s); a test threshold, not a game value.</summary>
    private const float MovingSpeed = 60f;

    /// <summary>Drawn bone motion (uu, any bone against the skeleton) a moving pawn must show within a step.</summary>
    private const float AnimatedBoneMotion = 0.5f;

    private readonly Dictionary<string, ClipSeen> _clips = new(StringComparer.Ordinal);
    private readonly Dictionary<SimPawnView, PoseWatch> _watch = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _issues = [];
    private readonly List<string> _notes = [];
    private readonly string _side;
    private readonly int _bots;
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private string _step = "";
    private int _teardown = -1;
    private SmokeResult _final;
    private double _shotClock;

    public PlayAnimSmoke()
    {
        _side = string.Equals(System.Environment.GetEnvironmentVariable("SVM_ANIM_SIDE"), "merc", StringComparison.OrdinalIgnoreCase) ? "merc" : "spy";
        _bots = int.TryParse(System.Environment.GetEnvironmentVariable("SVM_ANIM_BOTS"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int b) ? b : 3;
    }

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
            LaunchOptions options = context.App.Options;
            var config = new PlayConfig
            {
                Settings = new PlaySettings
                {
                    Map = "SteeE",
                    Side = _side == "merc" ? PlaySide.Merc : PlaySide.Spy,
                    Rules = PlayRules.PlayLevel,
                    HostMode = PlayHostMode.Thread,
                    MapRoots = [.. options.MapRoots],
                    Bots = _bots,
                    Difficulty = "normal",
                },
                GameRoot = options.GameRoot,
                Hud = DisplayServer.GetName() != "headless",
            };
            _root = PlayRoot.Create(config);
            context.Tree.Root.AddChild(_root);
            return SmokeResult.Running;
        }

        if (_root.LoadError is { } error)
        {
            return Finish(SmokeResult.Fail("play scene did not load: " + error));
        }

        if (!_root.IsLoaded || _root.Session is not { } session || session.LocalPawn is null || _root.Pawns is null)
        {
            return context.ElapsedSeconds > 120 ? Finish(SmokeResult.Fail("no local pawn after 120 s")) : SmokeResult.Running;
        }

        if (session.ServerFailure is { } failure)
        {
            return Finish(SmokeResult.Fail("server failed: " + failure.Message));
        }

        if (_keys is null)
        {
            _keys = KeyPilot.Attach(_root);
            List<KeyStep> steps = Timeline(_side);
            if (_bots > 0 && double.TryParse(System.Environment.GetEnvironmentVariable("SVM_ANIM_WAIT"), NumberStyles.Float, CultureInfo.InvariantCulture, out double wait) && wait > 0)
            {
                // Time for the bots to find the player: hits, deaths and wake-ups on the local pawn.
                steps.Add(new KeyStep("with-bots", wait));
            }

            _keys.Play(steps);
            return SmokeResult.Running;
        }

        _keys.Update();
        if (_keys.Step != _step)
        {
            _step = _keys.Step;
            foreach (PoseWatch w in _watch.Values)
            {
                w.NewStep(_step);
            }
        }

        double dt = 1.0 / Math.Max(1.0, Engine.GetFramesPerSecond());
        if (_keys.Ready && _step.Length > 0)
        {
            Record(session, _root.Pawns, dt);
            RecordFirstPerson(session);
        }

        SaveShot(dt);
        if (!_keys.Done)
        {
            return context.ElapsedSeconds > 900 ? Finish(SmokeResult.Fail("the timeline did not finish in 900 s")) : SmokeResult.Running;
        }

        return Finish(Verdict(session));
    }

    /// <summary>The moveset played, as the side's bound keys.</summary>
    private static List<KeyStep> Timeline(string side)
    {
        var steps = new List<KeyStep>
        {
            new("idle", 3.0),
            new("look", 2.0) { LookPerSecond = new Vector2(250f, 0f) },
            new("walk", 3.0) { Hold = ["BT_Up"] },
            new("run", 3.0) { Hold = ["BT_Up"], Tap = ["ChangeSpeed"] },
            new("run-stop", 1.5),
            new("back", 2.0) { Hold = ["BT_Down"] },
            new("left", 2.0) { Hold = ["BT_Left"] },
            new("right", 2.0) { Hold = ["BT_Right"] },
            new("crouch", 2.0) { Tap = ["Crouch"] },
            new("crouch-walk", 3.0) { Hold = ["BT_Up"] },
            new("crouch-idle", 1.5),
            new("stand", 2.0) { Tap = ["Crouch"] },
            new("jump", 2.0) { Tap = ["Jump"] },
            new("run-jump", 2.0) { Hold = ["BT_Up"], Tap = ["Jump"] },
            new("roll", 2.0) { Hold = ["BT_Up"], Tap = ["Roll"] },
            new("idle-2", 2.0),
        };
        if (side == "merc")
        {
            steps.Add(new KeyStep("fire", 2.0) { Tap = ["Fire"] });
            steps.Add(new KeyStep("reload", 3.0) { Tap = ["Reload"] });
        }
        else
        {
            steps.Add(new KeyStep("draw", 2.0) { Tap = ["FireMode"] });
            steps.Add(new KeyStep("draw-walk", 2.0) { Hold = ["BT_Up"] });
            steps.Add(new KeyStep("fire", 2.0) { Tap = ["Fire"] });
            steps.Add(new KeyStep("holster", 2.0) { Tap = ["FireMode"] });
        }

        steps.Add(new KeyStep("idle-end", 2.0));
        return steps;
    }

    private void Record(PlaySession session, PawnViews views, double dt)
    {
        SBasePawn? local = session.LocalPawn;
        IPawnAnimationSource? source = MovementService.Of(local!)?.Animations;
        foreach (SimPawnView view in views.Views)
        {
            if (view.Pawn is not { } pawn || view.Model is not { } model || !view.Visible)
            {
                continue;
            }

            bool isLocal = ReferenceEquals(pawn, local);
            string who = isLocal ? "local" : (pawn is SPAWNDEFENSE ? "merc" : "spy") + "-bot";
            if (!_watch.TryGetValue(view, out PoseWatch? watch))
            {
                watch = new PoseWatch(who + "#" + pawn.ActorId.ToString(CultureInfo.InvariantCulture), isLocal);
                watch.NewStep(_step);
                _watch[view] = watch;
            }

            watch.Frame(view, pawn, dt, isLocal ? $"up{session.Input.Get("BT_Up")}" : "");
            PawnAnimation anim = pawn.PawnAnim;
            for (int c = 0; c < PawnAnimation.ChannelCount; c++)
            {
                PawnAnimChannel ch = anim[c];
                if (ch.Sequence.IsNone)
                {
                    continue;
                }

                float alpha = c == 0 ? 1f : ch.BlendAlpha;
                string seq = ch.Sequence.ToString();
                string key = string.Create(CultureInfo.InvariantCulture, $"{who}|{(isLocal ? _step : "-")}|{c}|{seq}");
                if (!_clips.TryGetValue(key, out ClipSeen? seen))
                {
                    bool inSource = source is not null && source.TryGetClip(pawn, ch.Sequence, out _);
                    bool inMesh = model.Sampler.Find(seq) is not null;
                    seen = new ClipSeen(who, isLocal ? _step : "-", c, seq, inSource, inMesh);
                    _clips[key] = seen;
                }

                seen.Frames++;
                if (alpha > 0f)
                {
                    seen.DrawnFrames++;
                    seen.MaxAlpha = Math.Max(seen.MaxAlpha, alpha);
                }
            }
        }
    }

    /// <summary>The merc's first-person view model: its channels, whether the view shows it and has the clips.</summary>
    private void RecordFirstPerson(PlaySession session)
    {
        if (_root?.FirstPerson is not { } fp || session.LocalPawn?.MyWeapon is not OpenScct.Game.SBase.SWeaponDefense weapon)
        {
            return;
        }

        string step = _step;
        string shownKey = "fp|" + step + "|shown";
        if (!_clips.TryGetValue(shownKey, out ClipSeen? shown))
        {
            shown = new ClipSeen("fp", step, -1, fp.Shown ? "(shown)" : "(hidden)", true, true);
            _clips[shownKey] = shown;
        }

        shown.Frames++;
        if (fp.Shown)
        {
            shown.DrawnFrames++;
        }

        OpenScct.Game.Equipment.ViewModelAnim anim = weapon.ViewAnim;
        for (int c = 0; c < OpenScct.Game.Equipment.ViewModelAnim.ChannelCount; c++)
        {
            Name seq = anim.Sequence(c);
            if (seq.IsNone)
            {
                continue;
            }

            float alpha = c == 0 ? 1f : anim.Alpha(c);
            string key = string.Create(CultureInfo.InvariantCulture, $"fp|{step}|{c}|{seq}");
            if (!_clips.TryGetValue(key, out ClipSeen? seen))
            {
                bool inMesh = fp.Model?.Sampler.Find(seq.ToString()) is not null;
                seen = new ClipSeen("fp", step, c, seq.ToString(), true, inMesh || fp.Model is null);
                _clips[key] = seen;
            }

            seen.Frames++;
            if (alpha > 0f && fp.Shown)
            {
                seen.DrawnFrames++;
                seen.MaxAlpha = Math.Max(seen.MaxAlpha, alpha);
            }
        }
    }

    private SmokeResult Verdict(PlaySession session)
    {
        // The local pawn and its first-person view model fail the run; what other pawns show is reported (the
        // proxies' channels are the proxy playback's).
        foreach (ClipSeen s in _clips.Values)
        {
            List<string> into = s.Who is "local" or "fp" ? _issues : _notes;
            if (s.DrawnFrames > 0 && !s.InMesh)
            {
                into.Add($"{s.Who} step {s.Step} c{s.Channel}: {s.Sequence} is drawn but the mesh has no such clip");
            }
            else if (s.DrawnFrames > 0 && !s.InSource)
            {
                into.Add($"{s.Who} step {s.Step} c{s.Channel}: {s.Sequence} is a stand-in (the animation set has no such clip)");
            }
        }

        foreach (PoseWatch w in _watch.Values)
        {
            (w.Local ? _issues : _notes).AddRange(w.Issues());
        }

        string table = Table(session);
        try
        {
            string dir = SmokePaths.Local("anim-play");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, _side + ".md"), table);
        }
        catch (IOException e)
        {
            GD.PushWarning("play-anim: could not write the table: " + e.Message);
        }

        foreach (string issue in _issues)
        {
            GD.Print("SVM_ANIM " + issue);
        }

        foreach (string note in _notes)
        {
            GD.Print("SVM_ANIM note: " + note);
        }

        string summary = string.Create(CultureInfo.InvariantCulture,
            $"side={_side} bots={_bots} clips={_clips.Count} pawns={_watch.Count} delivered={_keys?.Delivered} notes={string.Join(",", _keys?.Notes ?? [])} missing={string.Join(",", _keys?.Missing ?? [])}");
        return _issues.Count == 0 ? SmokeResult.Pass(summary) : SmokeResult.Fail($"{_issues.Count} issues: {string.Join(" | ", _issues.Take(12))} ({summary})");
    }

    private string Table(PlaySession session)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Clips drawn in play ({_side}, {_bots} bots)");
        sb.AppendLine();
        sb.AppendLine("| Pawn | Step | Channel | Sequence | Frames | Drawn | Max alpha | In set | In mesh |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (ClipSeen s in _clips.Values.OrderBy(s => s.Who, StringComparer.Ordinal).ThenBy(s => s.FirstOrder).ThenBy(s => s.Channel))
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {s.Who} | {s.Step} | {s.Channel} | {s.Sequence} | {s.Frames} | {s.DrawnFrames} | {s.MaxAlpha:0.##} | {(s.InSource ? "yes" : "NO")} | {(s.InMesh ? "yes" : "NO")} |");
        }

        sb.AppendLine();
        sb.AppendLine("| Pawn | Step | Frames | Moving frames | Max speed | Max bone motion (uu) | Lowest bone over feet (uu) | States |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (PoseWatch w in _watch.Values)
        {
            foreach (PoseWatch.StepStats st in w.Steps)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {w.Label} | {st.Step} | {st.Frames} | {st.MovingFrames} | {st.MaxSpeed:0} | {st.MaxBoneMotion:0.0} | {st.MinFoot:0.0} to {st.MaxFoot:0.0} | {string.Join(" ", st.States)} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Issues (local pawn and view model):");
        foreach (string i in _issues)
        {
            sb.AppendLine("- " + i);
        }

        sb.AppendLine();
        sb.AppendLine("Notes (other pawns):");
        foreach (string n in _notes)
        {
            sb.AppendLine("- " + n);
        }

        return sb.ToString();
    }

    private void SaveShot(double dt)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null)
        {
            return;
        }

        _shotClock -= dt;
        if (_shotClock > 0)
        {
            return;
        }

        _shotClock = 1.0;
        Directory.CreateDirectory(folder);
        Image image = _root.GetViewport().GetTexture().GetImage();
        string label = _step.Length > 0 ? _step : "wait";
        image.SavePng(Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"anim-{_side}-{Time.GetTicksMsec():D8}-{label}.png")));
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _keys?.ReleaseAll();
        _final = result;
        _teardown = 0;
        _root?.QueueFree();
        return SmokeResult.Running;
    }

    private sealed class ClipSeen(string who, string step, int channel, string sequence, bool inSource, bool inMesh)
    {
        private static int s_order;

        public string Who { get; } = who;

        public string Step { get; } = step;

        public int Channel { get; } = channel;

        public string Sequence { get; } = sequence;

        public bool InSource { get; } = inSource;

        public bool InMesh { get; } = inMesh;

        public int FirstOrder { get; } = s_order++;

        public int Frames { get; set; }

        public int DrawnFrames { get; set; }

        public float MaxAlpha { get; set; }
    }

    /// <summary>The drawn pose of one pawn: how far its bones move against the skeleton per step, and its lowest bone.</summary>
    private sealed class PoseWatch(string label, bool local)
    {
        private readonly List<StepStats> _steps = [];
        private Vector3[]? _first;

        public string Label { get; } = label;

        public bool Local => _local;

        private readonly bool _local = local;

        public IReadOnlyList<StepStats> Steps => _steps;

        public void NewStep(string step)
        {
            _steps.Add(new StepStats { Step = _local ? step : "all" });
            _first = null;
            if (!_local && _steps.Count > 1)
            {
                // Bots keep one row: carry on the first.
                _steps.RemoveAt(_steps.Count - 1);
                _first = null;
            }
        }

        public void Frame(SimPawnView view, SBasePawn pawn, double dt, string input)
        {
            if (_steps.Count == 0 || view.Model is not { } model)
            {
                return;
            }

            StepStats st = _steps[^1];
            Skeleton3D sk = model.Skeleton;
            int n = sk.GetBoneCount();
            _first ??= new Vector3[n];
            bool firstFrame = st.Frames == 0 || (!_local && st.Frames % 60 == 0);
            float motion = 0f;
            float lowest = float.MaxValue;
            OpenScct.Core.FVector root = UeSpace.ToUe(sk.GlobalTransform * sk.GetBoneGlobalPose(0).Origin);
            for (int b = 0; b < n; b++)
            {
                OpenScct.Core.FVector w = UeSpace.ToUe(sk.GlobalTransform * sk.GetBoneGlobalPose(b).Origin);
                var rel = new Vector3(w.X - root.X, w.Y - root.Y, w.Z - root.Z);
                if (firstFrame)
                {
                    _first[b] = rel;
                }
                else
                {
                    motion = Math.Max(motion, (rel - _first[b]).Length());
                }

                lowest = Math.Min(lowest, w.Z);
            }

            st.Frames++;
            float speed = MathF.Sqrt((pawn.Velocity.X * pawn.Velocity.X) + (pawn.Velocity.Y * pawn.Velocity.Y));
            st.MaxSpeed = Math.Max(st.MaxSpeed, speed);
            if (speed > MovingSpeed)
            {
                st.MovingFrames++;
            }

            st.MaxBoneMotion = Math.Max(st.MaxBoneMotion, motion);
            if (_local)
            {
                st.States.Add($"{pawn.Controller?.GetStateName()}/{(int)pawn.Physics}{(pawn.bIsCrouched ? "c" : "")}/{input}/f{(int)((pawn.Controller as OpenScct.Core.Engine.PlayerController)?.aForward ?? 0)}/gs{(int)pawn.GroundSpeed}");
            }

            float foot = lowest - (pawn.Location.Z - pawn.CollisionHeight);
            st.MinFoot = float.IsNaN(st.MinFoot) ? foot : Math.Min(st.MinFoot, foot);
            st.MaxFoot = float.IsNaN(st.MaxFoot) ? foot : Math.Max(st.MaxFoot, foot);
        }

        public IEnumerable<string> Issues()
        {
            foreach (StepStats st in _steps)
            {
                if (st.MovingFrames > 30 && st.MaxBoneMotion < AnimatedBoneMotion)
                {
                    yield return string.Create(CultureInfo.InvariantCulture,
                        $"{Label} step {st.Step}: moved ({st.MovingFrames} frames, up to {st.MaxSpeed:0} uu/s) while no drawn bone moved (max {st.MaxBoneMotion:0.00} uu)");
                }
            }
        }

        public sealed class StepStats
        {
            public string Step { get; set; } = "";

            public int Frames { get; set; }

            public int MovingFrames { get; set; }

            public float MaxSpeed { get; set; }

            public float MaxBoneMotion { get; set; }

            public float MinFoot { get; set; } = float.NaN;

            public float MaxFoot { get; set; } = float.NaN;

            public SortedSet<string> States { get; } = new(StringComparer.Ordinal);
        }
    }
}
