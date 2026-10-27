using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScctGame.Controls;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Smoke;

namespace OpenScctGame.Audio;

/// <summary>
/// <c>--svm-smoke=play-audio</c> (spy) and <c>play-audio-merc</c>: what the player hears in an offline match, played
/// as the launcher plays it (SteeE, Play Level rules, three bots at normal, the listen server on its own thread, the
/// wall clock). The keys go in as Godot input events (Input.ParseInputEvent of the bound keys) to the play scene's
/// input, over loopback to the server: the player turns the wheel up to run speed, runs forward, jumps, and presses
/// Fire. The test then checks the
/// sound system of the play scene: it is bound to the client's world, and voices started for the player's own
/// footsteps and running gear, the landing, other pawns, the zone ambience and the spawn's interface sound (the
/// merc also for his rifle), and the audio buses and the output carried sound (their peak meters). Master is turned
/// 80 dB down for the run so a test makes no noise.
/// </summary>
public class PlayAudioSmoke : SmokeTest
{
    public const string TestName = "play-audio";

    [OpenScct.Provenance.Provenance(OpenScct.Provenance.ProvenanceKind.Decoded, "SBase.SGameInfo defaults PlayerStartSound (shared/audio classes)")]
    public const string SpawnSound = "Interface.Menu.Menu_start_point";

    private const int SettleTicks = 3 * Sim.Hz;
    private const int RunTicks = 4 * Sim.Hz;
    private const int JumpTicks = 2 * Sim.Hz;
    private const int FireTicks = 3 * Sim.Hz;
    private const int TeardownFrames = 6;

    private PlayRoot? _root;
    private int _phase;
    private long _phaseStart;
    private int _teardown = -1;
    private SmokeResult _final;
    private float _masterDb;
    private string _inputPath = "none";
    private readonly AudioPeaks _peaks = new();
    private readonly ServerSounds _server = new();
    private readonly List<string> _notes = [];

    public override string Name => TestName;

    protected virtual PlaySide Side => PlaySide.Spy;

    protected virtual string Map => "SteeE";

    /// <summary>
    /// Loops started more often than this in a run (about 16 s of play) are noted with their stops. A loop starts when
    /// it comes in range and keeps playing (rules 19 to 22); when more loops are in range than there are voices, the
    /// lowest priority ones lose their voice to other calls and win it back (rule 15): on Krauser's courtyard a far
    /// ambient loses its voice at each of the player's footsteps, a dozen or more times a run. Restarting every update
    /// (about a thousand times a run) would replay their first moments over and over.
    /// </summary>
    private const int MaxAmbientStartsPerActor = 20;

    /// <summary>Restarts of one loop with no stop the rules explain allowed in a run (the bug restarted it every update).</summary>
    private const int MaxUnexplainedRestarts = 2;

    public override SmokeResult Step(SmokeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_teardown >= 0)
        {
            return ++_teardown < TeardownFrames ? SmokeResult.Running : _final;
        }

        if (_root is null)
        {
            _masterDb = AudioPeaks.Quiet();
            bool headless = DisplayServer.GetName() == "headless";
            var config = new PlayConfig
            {
                Settings = new PlaySettings
                {
                    Map = Map,
                    Side = Side,
                    Rules = PlayRules.PlayLevel,
                    HostMode = PlayHostMode.Thread,
                    MapRoots = [.. context.App.Options.MapRoots],
                    Bots = 3,
                    Difficulty = "normal",
                },
                Render = true,
                Hud = !headless,
            };
            _root = PlayRoot.Create(config);
            context.Tree.Root.AddChild(_root);
            _notes.Add($"audio driver {AudioServer.OutputDevice} at {AudioServer.GetMixRate()} Hz, display {DisplayServer.GetName()}");
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

        _server.Install(session);
        AudioPeaks.Quiet();
        _peaks.Sample();
        SaveShot(context);
        long ticks = session.TicksRun;
        switch (_phase)
        {
            case 0:
                if (session.LocalPawn is not null && session.LocalController is { } pc && !pc.IsInState("WaitingForPawn"))
                {
                    Next(ticks);
                }
                else if (ticks > 60 * Sim.Hz)
                {
                    return Finish(SmokeResult.Fail("the local player never got a pawn"));
                }

                break;
            case 1 when ticks - _phaseStart >= SettleTicks:
                // The spy's spawn faces a wall: the mouse turns the view around first.
                _startYaw ??= session.LocalController!.Rotation.Yaw;
                if (Math.Abs(FRotator.NormalizeAxis(session.LocalController!.Rotation.Yaw - _startYaw.Value)) < 24000 && ticks - _phaseStart < SettleTicks + (3 * Sim.Hz))
                {
                    Look(60f);
                    break;
                }

                // The player stands up (the spy spawns crouched) and the wheel raises the movement speed to a run (a
                // walking step has no sound in the original).
                if (session.LocalPawn is { bIsCrouched: true })
                {
                    Press("C", true);
                    Press("C", false);
                }

                for (int i = 0; i < 8; i++)
                {
                    Press("MouseWheelUp", true);
                }

                Press("W", true);
                Next(ticks);
                break;
            case 2 when ticks - _phaseStart >= RunTicks:
                _notes.Add(string.Create(CultureInfo.InvariantCulture, $"after the run: pawn at {session.LocalPawn?.Location} speed {session.LocalPawn?.Velocity.Size():F0}"));
                Press("W", false);
                Press("Space", true);
                Next(ticks);
                break;
            case 3 when ticks - _phaseStart >= 6:
                Press("Space", false);
                Next(ticks);
                break;
            case 4 when ticks - _phaseStart >= JumpTicks:
                Press("LeftMouse", true);
                Next(ticks);
                break;
            case 5 when ticks - _phaseStart >= 12:
                Press("LeftMouse", false);
                Next(ticks);
                break;
            case 6 when ticks - _phaseStart >= FireTicks:
                return Finish(Evaluate(_root, session));
        }

        return SmokeResult.Running;
    }

    private int? _startYaw;

    /// <summary>The mouse moves right by <paramref name="pixels"/>, as a Godot motion event (or into the input directly without a window).</summary>
    private void Look(float pixels)
    {
        PlayInput? controls = _root?.Controls;
        if (controls is null)
        {
            return;
        }

        if (DisplayServer.GetName() != "headless" && !controls.IsCaptured)
        {
            OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Captured;
        }

        if (DisplayServer.GetName() != "headless" && controls.IsCaptured && controls.Enabled)
        {
            Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(pixels, 0f), ScreenRelative = new Vector2(pixels, 0f) });
            Input.FlushBufferedEvents();
            return;
        }

        if (_root?.Session is { } session)
        {
            session.Input.MouseX += pixels;
        }
    }

    private void Next(long ticks)
    {
        _phase++;
        _phaseStart = ticks;
    }

    /// <summary>
    /// A bound key goes down or up as the player's keyboard or mouse sends it: a Godot input event into the
    /// viewport, which the play scene's input reads while the pointer is captured. Without a window the pointer
    /// cannot be captured, so the event's key is handed to the same input node directly.
    /// </summary>
    private void Press(string ik, bool pressed)
    {
        PlayInput? controls = _root?.Controls;
        if (controls is null)
        {
            return;
        }

        if (DisplayServer.GetName() != "headless")
        {
            if (!controls.IsCaptured)
            {
                OpenScctGame.Controls.Pointer.Mode = global::Godot.Input.MouseModeEnum.Captured;
            }

            if (controls.IsCaptured && controls.Enabled)
            {
                InputEvent? ev = GodotKeys.ToEvent(ik);
                if (ev is InputEventKey key)
                {
                    key.Keycode = key.PhysicalKeycode;
                    key.Pressed = pressed;
                }
                else if (ev is InputEventMouseButton button)
                {
                    button.Pressed = pressed;
                }

                if (ev is not null)
                {
                    Input.ParseInputEvent(ev);
                    Input.FlushBufferedEvents();
                    _inputPath = "godot events";
                    return;
                }
            }
        }

        controls.Key(ik, pressed);
        _inputPath = "PlayInput.Key (no captured pointer)";
    }

    private SmokeResult Evaluate(PlayRoot root, PlaySession session)
    {
        SoundSystem? sound = FindSound(root);
        _notes.Add($"input via {_inputPath}");
        _notes.Add("server sounds: " + _server.Summary());
        _notes.Add("buses: " + _peaks.Summary());
        if (sound is null)
        {
            return SmokeResult.Fail("the play scene has no sound system; " + string.Join("; ", _notes));
        }

        Level level = session.Client.World.Level;
        int local = session.LocalPawn?.ActorId ?? -1;
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (SoundStart s in sound.Starts)
        {
            string kind = Kind(s, local, level);
            counts[kind] = counts.TryGetValue(kind, out int n) ? n + 1 : 1;
        }

        var missing = new List<string>();
        void Need(string kind)
        {
            if (!counts.ContainsKey(kind))
            {
                missing.Add(kind);
            }
        }

        // The player's steps reach the sound system; at a walk they name SW_FootWalk, which the original's packages
        // lack (silent in the original too), so only a run's steps start a voice.
        int ownStepCalls = 0;
        int heard = 0;
        int flat = 0;
        foreach (SoundCall c in sound.Calls)
        {
            ownStepCalls += c.ActorId == local && c.Sound.Contains(".FootSteps.SW_Foot", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            heard += c.Heard ? 1 : 0;
            flat += c.Flat ? 1 : 0;
        }

        if (ownStepCalls == 0 && Side == PlaySide.Merc)
        {
            missing.Add("own-footstep calls");
        }

        string silentTop = string.Join(",", sound.Calls.Where(c => c.Outcome == SoundCallOutcome.Silent)
            .GroupBy(c => c.Sound[(c.Sound.LastIndexOf('.') + 1)..]).OrderByDescending(g => g.Count()).Take(6).Select(g => $"{g.Key}={g.Count()}"));
        _notes.Insert(0, $"calls {sound.Calls.Count} (own steps {ownStepCalls}, from the server {heard}, 2D {flat}; silent {silentTop})");
        Need("own-landing");
        Need("ambient");
        Need("interface");
        Need("other-pawn");
        if (Side == PlaySide.Merc)
        {
            // The merc runs (the spy's movement speed stays at a walk or a crouch in the port for now) and shoots.
            Need("own-footstep");
            Need("own-weapon");
        }

        // Each start of a loop after its first needs a stop the rules explain (its voice taken by another call, rule 15,
        // or it left the range or its zone, rules 20 to 22); a loop restarted while it still played is the port's bug.
        var startCounts = new Dictionary<(int, string), int>();
        foreach (SoundStart s in sound.Starts)
        {
            if (s.Ambient)
            {
                startCounts[(s.ActorId, s.Call)] = startCounts.TryGetValue((s.ActorId, s.Call), out int n) ? n + 1 : 1;
            }
        }

        int Unexplained(int actor, int starts) => Math.Max(0, starts - 1 - (sound.AmbientStops.TryGetValue(actor, out int stops) ? stops : 0));
        string restarted = string.Join(",", startCounts.Where(kv => Unexplained(kv.Key.Item1, kv.Value) > MaxUnexplainedRestarts)
            .OrderByDescending(kv => kv.Value).Take(8).Select(kv => string.Create(CultureInfo.InvariantCulture,
                $"{kv.Key.Item2}@{kv.Key.Item1}x{kv.Value} ({Unexplained(kv.Key.Item1, kv.Value)} without a stop)")));
        foreach (var kv in startCounts.Where(kv => kv.Value > MaxAmbientStartsPerActor))
        {
            _notes.Add(string.Create(CultureInfo.InvariantCulture,
                $"{kv.Key.Item2}@{kv.Key.Item1} started {kv.Value} times, stopped by the rules {(sound.AmbientStops.TryGetValue(kv.Key.Item1, out int st) ? st : 0)} times"));
        }

        string started = string.Join(" ", counts.Select(kv => $"{kv.Key}={kv.Value}"));
        string unknown = sound.Unknown.Count > 0 ? " unknown=" + string.Join(",", sound.Unknown.Distinct().Take(8)) : "";
        string detail = string.Create(CultureInfo.InvariantCulture,
            $"voices started {sound.StartCount} ({started}) silent={sound.Silent}{unknown} zone={sound.ListenerZone} active={sound.ActiveVoices}; {string.Join("; ", _notes)}");
        GD.Print("SVM_AUDIO " + Name + " " + detail);
        if (missing.Count > 0)
        {
            return SmokeResult.Fail("no voice started for: " + string.Join(", ", missing) + "; " + detail);
        }

        if (restarted.Length > 0)
        {
            return SmokeResult.Fail("ambient loops restarted over and over: " + restarted + "; " + detail);
        }

        if (_peaks.LoudestSvm() < AudioPeaks.AudibleDb)
        {
            return SmokeResult.Fail("the sound buses stayed silent; " + detail);
        }

        // The output itself (Master, 80 dB down for the test) carried the mix.
        if (_peaks.Of("Master") <= AudioPeaks.Silence)
        {
            return SmokeResult.Fail("nothing reached the output (Master); " + detail);
        }

        return SmokeResult.Pass(detail);
    }

    /// <summary>What a started sound is, for the per-kind counts.</summary>
    internal static string Kind(in SoundStart s, int localPawn, Level level)
    {
        if (s.Music)
        {
            return "music";
        }

        if (s.Ambient)
        {
            return "ambient";
        }

        if (s.Call.StartsWith("Interface", StringComparison.OrdinalIgnoreCase))
        {
            return "interface";
        }

        string what = s.Call.Contains("SW_Landing", StringComparison.OrdinalIgnoreCase) ? "landing"
            : s.Call.Contains(".FootSteps.", StringComparison.OrdinalIgnoreCase) ? "footstep"
            : s.Call.Contains("vetements_materiel", StringComparison.OrdinalIgnoreCase) ? "gear"
            : s.Call.Contains("onomatopees", StringComparison.OrdinalIgnoreCase) ? "voice"
            : s.Call.StartsWith("Armes_impacts.Armes", StringComparison.OrdinalIgnoreCase) ? "weapon"
            : s.Call.StartsWith("Armes_impacts.Gadgets", StringComparison.OrdinalIgnoreCase) ? "gadget"
            : s.Call.StartsWith("Armes_impacts", StringComparison.OrdinalIgnoreCase) ? "impact"
            : "other";
        if (s.ActorId == localPawn)
        {
            return "own-" + what;
        }

        Actor? a = s.ActorId > 0 ? level.FindActor(s.ActorId) : null;
        if (a is Pawn || a?.Instigator is not null && a.Instigator.ActorId != localPawn)
        {
            return "other-pawn";
        }

        if (a is Controller c && c.Pawn?.ActorId == localPawn || a?.Instigator?.ActorId == localPawn)
        {
            return "own-" + what;
        }

        return what;
    }

    internal static SoundSystem? FindSound(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is SoundSystem s)
            {
                return s;
            }

            if (FindSound(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _final = result;
        _teardown = 0;
        _root?.QueueFree();
        AudioServer.SetBusVolumeDb(0, _masterDb);
        return SmokeResult.Running;
    }

    private void SaveShot(SmokeContext context)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null || context.Frame % 90 != 0)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(folder);
        Image image = _root.GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{Name}-{context.Frame:D5}.png")));
    }

    /// <summary>
    /// Counts the sound events the server's world raises (on the server's thread), so a report can tell sounds the
    /// client never heard from sounds nobody played.
    /// </summary>
    private sealed class ServerSounds : ISimEventSink
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);
        private bool _installed;
        private ISimEventSink? _next;

        public void Install(PlaySession session)
        {
            if (_installed || session.IsRemote)
            {
                return;
            }

            _installed = true;
            bool wrapped = false;
            session.Host.BeforeWorldTick += host =>
            {
                if (!wrapped)
                {
                    // Wraps the server's own sink (the hearing relay), which keeps every event.
                    wrapped = true;
                    _next = host.World.EventSink;
                    host.World.EventSink = this;
                }
            };
        }

        public void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events)
        {
            _next?.OnSimEvents(tick, events);
            lock (_gate)
            {
                foreach (SimEvent e in events)
                {
                    if (e.Kind != SimEventKind.PlaySound)
                    {
                        continue;
                    }

                    string name = e.What.ToString();
                    int dot = name.LastIndexOf('.');
                    string key = dot >= 0 ? name[(dot + 1)..] : name;
                    _counts[key] = _counts.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
        }

        public string Summary()
        {
            lock (_gate)
            {
                var sb = new StringBuilder();
                foreach (KeyValuePair<string, int> kv in _counts.OrderByDescending(kv => kv.Value).Take(16))
                {
                    sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
                }

                return sb.Length == 0 ? "none" : sb.ToString().TrimEnd();
            }
        }
    }
}

/// <summary><c>--svm-smoke=play-audio-merc</c>: <see cref="PlayAudioSmoke"/> as the merc, whose Fire also shoots the rifle.</summary>
public sealed class PlayAudioMercSmoke : PlayAudioSmoke
{
    public new const string TestName = "play-audio-merc";

    public override string Name => TestName;

    protected override PlaySide Side => PlaySide.Merc;
}

/// <summary>
/// <c>--svm-smoke=play-audio-krau</c>: <see cref="PlayAudioSmoke"/> as the merc on KrauE, whose merc spawn hears more
/// ambient loops at once than there are voices.
/// </summary>
public sealed class PlayAudioKrauSmoke : PlayAudioSmoke
{
    public new const string TestName = "play-audio-krau";

    public override string Name => TestName;

    protected override PlaySide Side => PlaySide.Merc;

    protected override string Map => "KrauE";
}
