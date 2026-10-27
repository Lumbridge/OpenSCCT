using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using OpenScct.Provenance;
using OpenScctGame.Audio;
using OpenScctGame.Hud;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>The checks a <see cref="PlayInspector"/> runs (docs/TESTING-PLAY.md).</summary>
[Flags]
public enum InspectChecks
{
    None = 0,
    Animation = 1,
    Feet = 2,
    Camera = 4,
    Audio = 8,
    Corrections = 16,
    Hud = 32,
    PauseMenu = 64,

    /// <summary>The keys did what they should to the local pawn (the smoke's own checks of the input path).</summary>
    Input = 128,
    All = Animation | Feet | Camera | Audio | Corrections | Hud | PauseMenu | Input,
}

/// <summary>One problem the inspector saw, merged by area and subject.</summary>
public sealed class InspectIssue
{
    internal InspectIssue(InspectChecks area, string subject, string message, string step, double seconds)
    {
        Area = area;
        Subject = subject;
        Message = message;
        FirstStep = step;
        FirstSeconds = seconds;
    }

    public InspectChecks Area { get; }

    public string Subject { get; }

    /// <summary>The message of the first sighting.</summary>
    public string Message { get; internal set; }

    /// <summary>The KeyPilot step it was first seen in.</summary>
    public string FirstStep { get; }

    /// <summary>Seconds into the inspection when it was first seen.</summary>
    public double FirstSeconds { get; }

    /// <summary>Frames, windows or calls it was seen in.</summary>
    public int Count { get; internal set; }

    /// <summary>The worst value seen (meaning depends on the check), or NaN.</summary>
    public float Worst { get; internal set; } = float.NaN;

    /// <summary>The worst sighting's message.</summary>
    public string WorstMessage { get; internal set; } = "";

    public override string ToString()
    {
        string worst = float.IsNaN(Worst) || WorstMessage == Message ? "" : " | worst: " + WorstMessage;
        return string.Create(CultureInfo.InvariantCulture,
            $"[{Area}] {Message} (seen {Count}x, first in step '{FirstStep}' at {FirstSeconds:F1} s){worst}");
    }
}

/// <summary>
/// Per-frame checks of what the player sees and hears in a play scene: drawn pawns animate and stand on the floor,
/// the camera is inside the level, sound plays, the server does not keep correcting the player, the HUD shows a page
/// and Escape opens a pause menu. It reads the scene and the client world, never writes them (except turning the
/// master bus down for quiet runs). See docs/TESTING-PLAY.md.
/// </summary>
public sealed class PlayInspector
{
    [Provenance(ProvenanceKind.Estimated, "Test threshold: the window over which pawn and bone motion are compared")]
    public const double InspectWindowSeconds = 0.5;

    [Provenance(ProvenanceKind.Estimated, "Test threshold: horizontal speed above which a pawn counts as moving (the spy's slowest walk is faster)")]
    public const float InspectMovingSpeed = 60f;

    [Provenance(ProvenanceKind.Estimated, "Test threshold: a bone of an animated walking pawn moves far more than this in half a second")]
    public const float InspectBoneMotion = 0.5f;

    [Provenance(ProvenanceKind.Estimated, "Test threshold: how far the lowest drawn bone may sink under the floor (skin and sole)")]
    public const float InspectFeetBelow = 8f;

    [Provenance(ProvenanceKind.Estimated, "Test threshold: how far the lowest drawn bone may sit over the floor (an ankle bone with the foot flat)")]
    public const float InspectFeetAbove = 30f;

    [Provenance(ProvenanceKind.Estimated, "Test threshold: how far under the collision bottom the floor is searched")]
    public const float InspectFloorSearch = 40f;

    /// <summary>
    /// The nearest the third-person camera may be to the local pawn's axis (uu): the original's m_absoluteMinDist
    /// (30) less the 8 uu our camera's wall sweeps still differ by (WallCameraTests).
    /// </summary>
    public const float InspectCameraNearest = 22f;

    [Provenance(ProvenanceKind.Estimated, "Test threshold: a client tick that moves the local pawn this much beyond its velocity is a snap")]
    public const float InspectSnapDistance = 40f;

    [Provenance(ProvenanceKind.Estimated, "Test threshold: corrections the client applies per 10 s of play (loopback has no loss or lag)")]
    public const float InspectCorrectionsPer10s = 2f;

    [Provenance(ProvenanceKind.Estimated, "Test threshold: a bus peak above this (dB) is sound")]
    public const float InspectPeakDb = -150f;

    /// <summary>The group a pause menu's node joins so the PauseMenu check finds it.</summary>
    public const string PauseMenuGroup = "svm_pause_menu";

    /// <summary>World geometry for traces (BSP, movers, static meshes).</summary>
    public const ETraceFlags WorldTrace = ETraceFlags.TRACE_Level | ETraceFlags.TRACE_Movers | ETraceFlags.TRACE_LevelGeometry;

    private readonly PlayRoot _root;
    private readonly Dictionary<string, InspectIssue> _issues = new(StringComparer.Ordinal);
    private readonly List<InspectIssue> _ordered = [];
    private readonly Dictionary<SimPawnView, InspectPose> _poses = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, int> _correctionsByStep = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _pages = new(StringComparer.Ordinal);
    private readonly List<SimPawnView> _gone = [];
    private InspectNet? _net;
    private SoundSystem? _sound;
    private double _seconds;
    private double _nextSoundSearch;
    private double _nextQuiet;
    private ulong _lastUsec;
    private long _clientTicks;
    private long _soundEvents;
    private long _snaps;
    private double _movingSeconds;
    private float _peakDb = -200f;
    private FVector _lastLocation;
    private SBasePawn? _lastPawn;
    private double _pauseDeadline = -1;
    private bool _pauseSeen;
    private bool _finished;
    private bool _realAudio;

    public PlayInspector(PlayRoot root, InspectChecks checks = InspectChecks.All)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        Checks = checks;
    }

    public InspectChecks Checks { get; }

    /// <summary>The step label issues are tagged with (set it from <see cref="KeyPilot.Step"/>).</summary>
    public string Phase { get; set; } = "";

    /// <summary>Turns the master bus down to -80 dB (peaks are still measured). On unless SVM_PLAY_AUDIBLE=1.</summary>
    public bool Quiet { get; set; } = System.Environment.GetEnvironmentVariable("SVM_PLAY_AUDIBLE") != "1";

    /// <summary>Raised when an issue is seen for the first time (for a screenshot).</summary>
    public event Action<InspectIssue>? IssueAdded;

    /// <summary>The issues so far, in the order first seen.</summary>
    public IReadOnlyList<InspectIssue> Issues => _ordered;

    /// <summary>Seconds inspected.</summary>
    public double Seconds => _seconds;

    /// <summary>Hooks the client tick and the move calls; call once the play scene is loaded.</summary>
    public void Attach()
    {
        PlaySession session = _root.Session ?? throw new InvalidOperationException("the play scene has no session (not loaded?)");
        Action<PlaySession, long>? after = session.AfterClientTick;
        session.AfterClientTick = (s, tick) =>
        {
            after?.Invoke(s, tick);
            OnClientTick(s);
        };
        _net = new InspectNet(session.Client)
        {
            Received = name =>
            {
                string step = Phase.Length == 0 ? "(none)" : Phase;
                _correctionsByStep[step] = (_correctionsByStep.TryGetValue(step, out int n) ? n : 0) + 1;
                if (name == "ClientReinitPawnBecauseOfDivergences" && (Checks & InspectChecks.Corrections) != 0)
                {
                    Note(InspectChecks.Corrections, "reinit", "the server reinit the local pawn (ClientReinitPawnBecauseOfDivergences): it diverged badly");
                }
            },
        };
        _realAudio = DisplayServer.GetName() != "headless";
        _lastUsec = Time.GetTicksUsec();
    }

    /// <summary>Asks the PauseMenu check to see a pause menu within <paramref name="seconds"/> (call it right after pressing Escape).</summary>
    public void ExpectPauseMenu(double seconds = 1.5)
    {
        _pauseDeadline = _seconds + seconds;
        _pauseSeen = false;
    }

    /// <summary>Records an issue (merged with earlier ones of the same area and subject).</summary>
    public void Note(InspectChecks area, string subject, string message, float value = float.NaN)
    {
        string key = area + "|" + subject;
        if (!_issues.TryGetValue(key, out InspectIssue? issue))
        {
            issue = new InspectIssue(area, subject, message, Phase.Length == 0 ? "(none)" : Phase, _seconds);
            _issues[key] = issue;
            _ordered.Add(issue);
            issue.Count = 1;
            issue.Worst = value;
            issue.WorstMessage = message;
            IssueAdded?.Invoke(issue);
            return;
        }

        issue.Count++;
        if (!float.IsNaN(value) && (float.IsNaN(issue.Worst) || value > issue.Worst))
        {
            issue.Worst = value;
            issue.WorstMessage = message;
        }
    }

    /// <summary>One frame of checks; call it after the play scene has drawn (PlayRoot processes before its children's later siblings).</summary>
    public void Frame()
    {
        ulong now = Time.GetTicksUsec();
        double dt = (now - _lastUsec) / 1_000_000.0;
        _lastUsec = now;
        _seconds += dt;
        if (_root.Session is not { } session)
        {
            return;
        }

        _net?.TryInstall(session.LocalController);
        CollisionWorld? collision = session.Client.Map?.Physics?.Collision;
        if ((Checks & (InspectChecks.Animation | InspectChecks.Feet)) != 0)
        {
            Poses(session, dt, collision);
        }

        if ((Checks & InspectChecks.Camera) != 0 && collision is not null)
        {
            CameraCheck(session, collision);
        }

        if ((Checks & InspectChecks.Audio) != 0)
        {
            AudioFrame();
        }

        if ((Checks & InspectChecks.Hud) != 0)
        {
            string page = _root.Hud is null ? "no HUD" : _root.Hud.ActivePage?.GetType().Name ?? "no page";
            _pages[page] = (_pages.TryGetValue(page, out int n) ? n : 0) + 1;
        }

        if ((Checks & InspectChecks.PauseMenu) != 0 && _pauseDeadline >= 0)
        {
            PauseFrame();
        }
    }

    private void Poses(PlaySession session, double dt, CollisionWorld? collision)
    {
        if (_root.Pawns is not { } pawns)
        {
            return;
        }

        SBasePawn? local = session.LocalPawn;
        foreach (SimPawnView view in pawns.Views)
        {
            if (!_poses.TryGetValue(view, out InspectPose? pose))
            {
                pose = new InspectPose(view, Label(view.Pawn, local));
                _poses[view] = pose;
            }

            pose.Frame(this, dt, collision, Checks);
        }

        _gone.Clear();
        foreach (SimPawnView view in _poses.Keys)
        {
            if (!GodotObject.IsInstanceValid(view) || view.Pawn is null || view.Pawn.bDeleteMe)
            {
                _gone.Add(view);
            }
        }

        foreach (SimPawnView view in _gone)
        {
            _poses.Remove(view);
        }
    }

    private static string Label(SBasePawn? pawn, SBasePawn? local)
    {
        if (pawn is null)
        {
            return "pawn";
        }

        string name = pawn.PlayerReplicationInfo?.PlayerName is { Length: > 0 } n ? n : pawn.ClassName.ToString() + pawn.ActorId;
        return ReferenceEquals(pawn, local) ? $"{name} (you, {pawn.ClassName})" : $"{name} ({pawn.ClassName})";
    }

    private void CameraCheck(PlaySession session, CollisionWorld collision)
    {
        if (_root.Camera is not { } camera || !session.View.Valid || session.LocalPawn is not { } pawn)
        {
            return;
        }

        FVector location = camera.Current.Location;
        bool behind = session.LocalController?.bBehindView ?? false;
        string mode = behind ? "third person" : "first person";
        var probe = new FVector(4f, 4f, 4f);
        if (collision.PointCheck(out FCheckResult inside, null, location, probe, WorldTrace))
        {
            Note(InspectChecks.Camera, "in solid " + mode,
                string.Create(CultureInfo.InvariantCulture, $"the {mode} camera at {location} is inside world geometry ({InspectPose.Stance(pawn)}, pawn at {pawn.Location})"));
            return;
        }

        if (!collision.LineCheck(out _, null, location - new FVector(0f, 0f, 20000f), location, default, WorldTrace))
        {
            Note(InspectChecks.Camera, "outside " + mode,
                string.Create(CultureInfo.InvariantCulture, $"the {mode} camera at {location} has no floor under it: it is outside the level"));
            return;
        }

        if (pawn.Physics == EPhysics.PHYS_Walking
            && collision.LineCheck(out FCheckResult floor, pawn, pawn.Location - new FVector(0f, 0f, pawn.CollisionHeight + InspectFloorSearch), pawn.Location, default, WorldTrace)
            && location.Z < floor.Location.Z - 2f)
        {
            Note(InspectChecks.Camera, "under floor " + mode,
                string.Create(CultureInfo.InvariantCulture,
                    $"the {mode} camera is {floor.Location.Z - location.Z:F0} uu under the floor the pawn stands on ({InspectPose.Stance(pawn)})"),
                floor.Location.Z - location.Z);
        }

        // The original pulls the camera in to m_absoluteMinDist (30 uu) from the camera target when a wall is right
        // behind the pawn, which puts it inside the pawn's cylinder and fills the screen with the pawn (captured:
        // tools/golden/poses/SteeE-walls.json, tests/OpenScctGame.Logic.Tests/Views/WallCameraTests.cs); a crouched pawn
        // can also be out of the camera's sight behind low walls there. Only a camera nearer the pawn's axis than the
        // original ever puts it is an issue; the hidden frames are counted.
        FVector rel = location - pawn.Location;
        float horizontal = MathF.Sqrt((rel.X * rel.X) + (rel.Y * rel.Y));
        if (behind && horizontal < InspectCameraNearest && Math.Abs(rel.Z) < pawn.CollisionHeight)
        {
            Note(InspectChecks.Camera, "inside pawn",
                string.Create(CultureInfo.InvariantCulture,
                    $"the third person camera is {horizontal:F0} uu from the local pawn's axis ({rel.Z:F0} uu over its centre; {InspectPose.Stance(pawn)}), nearer than the original ever puts it (30 uu): the screen shows the inside of the spy's head or body"),
                InspectCameraNearest - horizontal);
        }

        if (behind && collision.LineCheck(out FCheckResult hit, pawn, location, pawn.Location, default, WorldTrace) &&
            (location - hit.Location).Size() > 10f)
        {
            _hiddenPawnFrames++;
        }
    }

    private void AudioFrame()
    {
        if (_sound is null && _seconds >= _nextSoundSearch)
        {
            _nextSoundSearch = _seconds + 1.0;
            _sound = FindSound(_root.GetTree().Root);
        }

        if (Quiet && _seconds >= _nextQuiet)
        {
            _nextQuiet = _seconds + 1.0;
            AudioServer.SetBusVolumeDb(0, -80f);
        }

        if (!_realAudio)
        {
            return;
        }

        for (int bus = 0; bus < AudioServer.BusCount; bus++)
        {
            for (int ch = 0; ch < AudioServer.GetBusChannels(bus); ch++)
            {
                _peakDb = Math.Max(_peakDb, Math.Max(AudioServer.GetBusPeakVolumeLeftDb(bus, ch), AudioServer.GetBusPeakVolumeRightDb(bus, ch)));
            }
        }
    }

    private static SoundSystem? FindSound(Node node)
    {
        if (node is SoundSystem s)
        {
            return s;
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindSound(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void PauseFrame()
    {
        foreach (Node node in _root.GetTree().GetNodesInGroup(PauseMenuGroup))
        {
            _pauseSeen |= node switch
            {
                CanvasItem item => item.IsVisibleInTree(),
                CanvasLayer layer => layer.Visible,
                Node3D spatial => spatial.IsVisibleInTree(),
                _ => true,
            };
        }

        // A menu that has not joined the group yet still counts when its class says what it is.
        _pauseSeen |= FindMenu(_root.GetTree().Root) is not null;
        if (_pauseSeen)
        {
            _pauseDeadline = -1;
        }
        else if (_seconds > _pauseDeadline)
        {
            _pauseDeadline = -1;
            Note(InspectChecks.PauseMenu, "none",
                $"Escape ({KeyOfMenu()}) opened no pause menu: no visible node in the group '{PauseMenuGroup}'");
        }
    }

    /// <summary>A visible node whose class is named like a pause or in-game menu, or null.</summary>
    private static Node? FindMenu(Node node)
    {
        string type = node.GetType().Name;
        if ((type.Contains("PauseMenu", StringComparison.Ordinal) || type.Contains("InGameMenu", StringComparison.Ordinal))
            && !type.Contains("Smoke", StringComparison.Ordinal) && !type.Contains("Probe", StringComparison.Ordinal)
            && node switch { CanvasItem item => item.IsVisibleInTree(), CanvasLayer layer => layer.Visible, _ => true })
        {
            return node;
        }

        foreach (Node child in node.GetChildren())
        {
            if (FindMenu(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private string KeyOfMenu()
    {
        if (_root.Controls?.Bindings is { } b)
        {
            foreach (KeyValuePair<string, string> binding in b.Layout.Bindings)
            {
                if (binding.Key == "Escape")
                {
                    return "IK_Escape=" + binding.Value;
                }
            }
        }

        return "IK_Escape";
    }

    /// <summary>After a client tick (client thread): sound events, the local pawn's snaps and its moving time.</summary>
    private void OnClientTick(PlaySession session)
    {
        _clientTicks++;
        foreach (SimEvent e in session.Client.World.Events.Current)
        {
            if (e.Kind == SimEventKind.PlaySound)
            {
                _soundEvents++;
            }
        }

        if (session.LocalPawn is not { } pawn)
        {
            _lastPawn = null;
            return;
        }

        FVector v = pawn.Velocity;
        if ((v.X * v.X) + (v.Y * v.Y) > InspectMovingSpeed * InspectMovingSpeed)
        {
            _movingSeconds += Sim.DeltaTime;
        }

        if (ReferenceEquals(pawn, _lastPawn) && (Checks & InspectChecks.Corrections) != 0)
        {
            FVector step = pawn.Location - _lastLocation;
            float unexplained = (step - (v * Sim.DeltaTime)).Size();
            if (unexplained > InspectSnapDistance)
            {
                _snaps++;
                Note(InspectChecks.Corrections, "snap",
                    string.Create(CultureInfo.InvariantCulture,
                        $"the local pawn jumped {step.Size():F0} uu in one client tick ({unexplained:F0} uu more than its velocity explains; physics {pawn.Physics}, {InspectPose.Stance(pawn)}, from {_lastLocation} to {pawn.Location})"),
                    unexplained);
            }
        }

        _lastPawn = pawn;
        _lastLocation = pawn.Location;
    }

    /// <summary>The end-of-run checks (sound never played, HUD never shown, too many corrections). Idempotent.</summary>
    public void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        if ((Checks & InspectChecks.Audio) != 0)
        {
            if (_sound is null)
            {
                Note(InspectChecks.Audio, "no sound system",
                    string.Create(CultureInfo.InvariantCulture,
                        $"no SoundSystem in the scene: the {_soundEvents} sound events gameplay raised on the client in {_clientTicks} ticks were never played"));
            }
            else
            {
                if (_sound.Started.Count == 0)
                {
                    Note(InspectChecks.Audio, "no voice",
                        string.Create(CultureInfo.InvariantCulture,
                            $"the SoundSystem started no voice in {_seconds:F0} s ({_soundEvents} sound events on the client, {_sound.Unknown.Count} unknown sounds, {_sound.Silent} silent)"));
                }

                if (_realAudio && _peakDb <= InspectPeakDb)
                {
                    Note(InspectChecks.Audio, "silent buses",
                        string.Create(CultureInfo.InvariantCulture, $"no audio bus peaked above {InspectPeakDb:F0} dB during play (loudest {_peakDb:F0} dB)"));
                }
            }

            if (_soundEvents == 0)
            {
                Note(InspectChecks.Audio, "no sound events",
                    string.Create(CultureInfo.InvariantCulture, $"gameplay raised no sound event on the client in {_clientTicks} ticks (footsteps, gear)"));
            }
        }

        if ((Checks & InspectChecks.Corrections) != 0 && _net is not null)
        {
            if (!_net.Installed)
            {
                Note(InspectChecks.Corrections, "not watched", "the move calls could not be watched (no local controller)");
            }

            // The server forces a correction every second whatever the error (net-moves.md rule 12, decoded), so
            // what counts is how many the client had to apply: those at a saved move off by fErrorMax or more.
            long received = _net.Corrections + _net.ShortCorrections;
            long applied = _net.Applied(_root.Session?.LocalController);
            double allowed = Math.Max(InspectCorrectionsPer10s, InspectCorrectionsPer10s * _seconds / 10.0);
            if (applied > allowed)
            {
                Note(InspectChecks.Corrections, "corrections",
                    string.Create(CultureInfo.InvariantCulture,
                        $"the server sent {received} position corrections to the local player in {_seconds:F0} s ({_net.ShortCorrections} short, {_net.Corrections} full; the client applied {applied}; {_movingSeconds:F0} s moving; allowed {allowed:F0} applied): by step {ByStep()}"));
            }
        }

        if ((Checks & InspectChecks.Hud) != 0)
        {
            bool sidePage = _pages.ContainsKey(nameof(SpyHudPage)) || _pages.ContainsKey(nameof(MercHudPage));
            if (_root.Hud is null)
            {
                Note(InspectChecks.Hud, "no HUD", "the play scene has no HUD (the UI content did not load)");
            }
            else if (!sidePage)
            {
                Note(InspectChecks.Hud, "no page", "the HUD never showed the spy or merc page: " + Pages());
            }
        }

        foreach (InspectPose pose in _poses.Values)
        {
            if ((Checks & InspectChecks.Animation) != 0 && pose.MovingWindows > 0 && pose.AnimatedWindows == 0)
            {
                Note(InspectChecks.Animation, "never animated " + pose.Label,
                    string.Create(CultureInfo.InvariantCulture,
                        $"{pose.Label} moved in {pose.MovingWindows} windows and was never drawn animating (largest bone motion {pose.MaxBoneMotion:F2} uu)"));
            }
        }
    }

    private string ByStep()
    {
        var sb = new StringBuilder();
        foreach (KeyValuePair<string, int> kv in _correctionsByStep)
        {
            sb.Append(sb.Length == 0 ? "" : ", ").Append(kv.Key).Append('=').Append(kv.Value.ToString(CultureInfo.InvariantCulture));
        }

        return sb.Length == 0 ? "none" : sb.ToString();
    }

    private string Pages()
    {
        var sb = new StringBuilder();
        foreach (KeyValuePair<string, int> kv in _pages)
        {
            sb.Append(sb.Length == 0 ? "" : ", ").Append(kv.Key).Append(' ').Append(kv.Value.ToString(CultureInfo.InvariantCulture)).Append(" frames");
        }

        return sb.Length == 0 ? "no frames" : sb.ToString();
    }

    /// <summary>The counters on one line.</summary>
    private int _hiddenPawnFrames;

    public string Summary()
    {
        var sb = new StringBuilder();
        sb.Append(string.Create(CultureInfo.InvariantCulture,
            $"seconds={_seconds:F1} clientTicks={_clientTicks} moving={_movingSeconds:F1}s corrections={_net?.Corrections ?? 0} short={_net?.ShortCorrections ?? 0} applied={_net?.Applied(_root.Session?.LocalController) ?? 0} reinits={_net?.Reinits ?? 0} snaps={_snaps} soundEvents={_soundEvents} soundSystem={(_sound is null ? "none" : "yes")} voices={_sound?.Started.Count ?? 0} peak={(_realAudio ? _peakDb.ToString("F0", CultureInfo.InvariantCulture) + "dB" : "n/a")} hud=[{Pages()}] hiddenPawnFrames={_hiddenPawnFrames}"));
        foreach (InspectPose pose in _poses.Values)
        {
            sb.Append(string.Create(CultureInfo.InvariantCulture,
                $" | {pose.Label}: animated {pose.AnimatedWindows}/{pose.MovingWindows} bone max {pose.MaxBoneMotion:F1}uu feet {pose.MinFoot:F1}..{pose.MaxFoot:F1}uu"));
        }

        return sb.ToString();
    }

    /// <summary>Every issue on its own line, then the counters. Runs <see cref="Finish"/> first.</summary>
    public string Report()
    {
        Finish();
        var sb = new StringBuilder();
        sb.Append(string.Create(CultureInfo.InvariantCulture, $"{_ordered.Count} issues"));
        foreach (InspectIssue issue in _ordered)
        {
            sb.Append('\n').Append("  ").Append(issue);
        }

        sb.Append('\n').Append("  ").Append(Summary());
        return sb.ToString();
    }
}
