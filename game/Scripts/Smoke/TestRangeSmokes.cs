using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Godot;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;
using OpenScctGame.Views;

namespace OpenScctGame.Smoke;

/// <summary>
/// TestRange, the test map (docs/TESTMAP.md): its station manifest (fixtures/testrange/stations.json) and the play
/// setup the TestRange smokes share. The map folder is made by <c>python -B tools/testmap/generate.py</c>.
/// </summary>
public static class TestRange
{
    public const string Map = "TestRange";

    /// <summary>One station of the manifest: its spot (feet), facing and the raw entry.</summary>
    public sealed record Station(string Id, string Kind, FVector Spot, int Yaw, JsonElement Entry)
    {
        public float Number(string key) => Entry.GetProperty(key).GetSingle();

        public FVector Vector(string key)
        {
            JsonElement v = Entry.GetProperty(key);
            return new FVector(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());
        }
    }

    public static string ManifestPath => SmokePaths.Tracked(Path.Combine("fixtures", "testrange", "stations.json"));

    /// <summary>The stations by id.</summary>
    public static Dictionary<string, Station> Stations()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(ManifestPath));
        var out_ = new Dictionary<string, Station>(StringComparer.Ordinal);
        foreach (JsonElement s in doc.RootElement.GetProperty("stations").EnumerateArray())
        {
            JsonElement spot = s.GetProperty("spot");
            out_[s.GetProperty("id").GetString()!] = new Station(s.GetProperty("id").GetString()!, s.GetProperty("kind").GetString()!,
                new FVector(spot[0].GetSingle(), spot[1].GetSingle(), spot[2].GetSingle()), s.GetProperty("yaw").GetInt32(), s.Clone());
        }

        return out_;
    }

    /// <summary>
    /// A play scene on TestRange the way <c>--svm-play=TestRange --side=... --rules=playlevel --bots=N</c> starts one
    /// (host thread, wall clock, HUD), added to the tree.
    /// </summary>
    public static PlayRoot Start(SmokeContext context, PlaySide side, int bots, string difficulty = "normal")
    {
        ArgumentNullException.ThrowIfNull(context);
        PlayConfig launcher = PlayConfig.FromOptions(context.App.Options);
        PlayConfig config = launcher with
        {
            Settings = launcher.Settings with
            {
                Map = Map,
                Side = side,
                Mode = "Story",
                Rules = PlayRules.PlayLevel,
                HostMode = PlayHostMode.Thread,
                Bots = bots,
                Difficulty = difficulty,
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

    /// <summary>
    /// On the server's thread, before its next world tick: puts <paramref name="pawnOf"/>'s pawn with its feet at
    /// <paramref name="feet"/> facing <paramref name="yaw"/>, still, and reinitialises that player's client there, as a
    /// respawn does. Returns at once; the move happens on the next server tick.
    /// </summary>
    public static void Place(PlaySession session, Func<ServerHost, SPlayerController?> pawnOf, FVector feet, int yaw)
    {
        ArgumentNullException.ThrowIfNull(session);
        Action<ServerHost>? once = null;
        once = host =>
        {
            host.BeforeWorldTick -= once;
            if (pawnOf(host) is not { Pawn: SBasePawn pawn } pc)
            {
                return;
            }

            FVector at = feet + new FVector(0f, 0f, pawn.CollisionHeight + 2f);
            at = new FVector(MathF.Round(at.X), MathF.Round(at.Y), MathF.Round(at.Z));
            MovementNatives.FarMoveActor(pawn, at);
            pawn.Velocity = default;
            pawn.Acceleration = default;
            pawn.Rotation = new FRotator(0, yaw, 0);
            Face(pc, yaw);
            if (host.World.Level.GetService<IMovementNet>() is NetMovementBridge bridge && bridge.EndpointOf(pc)?.Server is { } moves)
            {
                moves.ReinitPawn();
            }
        };
        session.Host.BeforeWorldTick += once;
    }

    /// <summary>
    /// Turns a controller to <paramref name="yaw"/>, level: its view rotation and the aim a merc's view follows
    /// (MyDesiredRotation and LocalMyDesiredRotation, which the mouse moves).
    /// </summary>
    public static void Face(SPlayerController pc, int yaw)
    {
        ArgumentNullException.ThrowIfNull(pc);
        var r = new FRotator(0, yaw, 0);
        pc.Rotation = r;
        pc.MyDesiredRotation = r;
        pc.LocalMyDesiredRotation = r;
    }

    /// <summary>With a window and <c>SVM_PLAY_SHOTS</c> naming a folder (keep it under local/), saves the window.</summary>
    public static void Shot(PlayRoot? root, string test, double seconds, string label)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || root is null)
        {
            return;
        }

        Directory.CreateDirectory(folder);
        string safe = string.Concat(label.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_');
        Image image = root.GetViewport().GetTexture().GetImage();
        image.SavePng(Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{test}-{seconds:000.0}-{safe}.png")));
    }

    /// <summary>True once the local player's controller is in a walking state with its pawn (keys reach it from then on).</summary>
    public static bool Walking(SPlayerController? pc) =>
        pc?.Pawn is SBasePawn && pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal);
}

/// <summary>
/// <c>--svm-smoke=range-traversal</c>: play-traversal on TestRange. For each station the server places the spy on the
/// station's spot and reinitialises the client there, then <see cref="KeyPilot"/> plays a recorded scenario's inputs
/// (tests/scenarios) as key presses: the ledge pull-up at Ledge264, the zip line at Zip1760, the pipe climb at Pipe512
/// and the ladder climb at Ladder320 (each station copies the stock spot the scenario was recorded at). Fails on a
/// reinit, on a correction during the move, or when the spy does not end where the move takes him (on the block top,
/// on the landing deck). With a window and <c>SVM_PLAY_SHOTS</c> it saves the window every half second.
/// </summary>
public sealed class RangeTraversalSmoke : SmokeTest
{
    public const string TestName = "range-traversal";

    private const double SpawnTimeoutSeconds = 90.0;
    private const double SettleSeconds = 3.0;
    private const int TeardownFrames = 6;

    private sealed record Plan(string Station, string Scenario, FVector Spot, int Yaw, bool Fast, List<KeyStep> Steps, Func<SBasePawn, HashSet<string>, string?> Judge);

    private readonly List<Plan> _plans = new();
    private readonly List<string> _log = new();
    private readonly List<string> _failures = new();
    private readonly HashSet<string> _states = new(StringComparer.Ordinal);
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private InspectNet? _net;
    private int _plan = -1;
    private double _phaseStart;
    private bool _playing;
    private bool _placeQueued;
    private long _reinitsAtStart;
    private long _appliedAtStart;
    private long _lastApplied;
    private int _sensitive;
    private double _nextShot;
    private SmokeResult _final;
    private int _teardown = -1;

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
            try
            {
                Dictionary<string, TestRange.Station> st = TestRange.Stations();
                TestRange.Station ledge = st["Ledge264"];
                float top = ledge.Number("height");
                _plans.Add(Load(st["Ledge264"], "traversal/spy-ledge-pull-up",
                    (p, s) => s.Contains("TopLedge") && p.Location.Z - p.CollisionHeight > top - 4f ? null : $"did not climb onto the 264 top (z {p.Location.Z})"));
                TestRange.Station zip = st["Zip1760"];
                float landing = zip.Number("landingTop");
                FVector low = zip.Vector("low");
                _plans.Add(Load(zip, "traversal/spy-zip-line-29",
                    (p, s) => s.Contains("s_ZipLine") && p.Location.Z - p.CollisionHeight > landing - 4f && MathF.Abs(p.Location.Y - low.Y) < 300f
                        ? null : $"did not ride the line onto the landing deck (end {p.Location})"));
                float pipeTop = st["Pipe512"].Number("height");
                _plans.Add(Load(st["Pipe512"], "traversal/spy-pipe-climb-up",
                    (p, s) => p.Location.Z - p.CollisionHeight > pipeTop - 4f ? null : $"did not climb the pipe onto the 512 top (z {p.Location.Z})"));
                float ladderTop = st["Ladder320"].Number("height");
                _plans.Add(Load(st["Ladder320"], "moveset/spy-ladder-up",
                    (p, s) => p.Location.Z - p.CollisionHeight > ladderTop - 4f ? null : $"did not climb the ladder onto the 320 top (z {p.Location.Z})"));
            }
            catch (Exception e) when (e is IOException or KeyNotFoundException or JsonException)
            {
                return SmokeResult.Fail("manifest or scenario not readable: " + e.Message);
            }

            _root = TestRange.Start(context, PlaySide.Spy, bots: 0);
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
            if (!TestRange.Walking(pc))
            {
                return context.ElapsedSeconds > SpawnTimeoutSeconds ? Finish(SmokeResult.Fail("the spy was not drawn in a walking state")) : SmokeResult.Running;
            }

            _keys = KeyPilot.Attach(_root);
            _net = new InspectNet(session.Client);
            _net.TryInstall(pc);
            _phaseStart = context.ElapsedSeconds;
            return SmokeResult.Running;
        }

        _keys.Update();
        if (context.ElapsedSeconds >= _nextShot)
        {
            _nextShot = context.ElapsedSeconds + 0.5;
            TestRange.Shot(_root, TestName, context.ElapsedSeconds, _playing && _plan >= 0 ? _plans[_plan].Station + "-" + pc?.GetStateName() : "setup");
        }

        if (!_playing)
        {
            return Place(context, session, pc);
        }

        Watch(pc);
        if (!_keys.Done)
        {
            return SmokeResult.Running;
        }

        EndPhase(pc);
        _playing = false;
        _placeQueued = false;
        _phaseStart = context.ElapsedSeconds;
        if (_plan + 1 < _plans.Count)
        {
            return SmokeResult.Running;
        }

        foreach (string line in _log)
        {
            GD.Print("SVM_RANGE " + line);
        }

        GD.Print($"SVM_RANGE keys: {string.Join("; ", _keys.Notes)}; sent {_keys.Sent} delivered {_keys.Delivered}");
        return Finish(_failures.Count == 0
            ? SmokeResult.Pass(string.Join(" | ", _log))
            : SmokeResult.Fail(string.Join(" | ", _failures) + " || " + string.Join(" | ", _log)));
    }

    private SmokeResult Place(SmokeContext context, PlaySession session, SPlayerController? pc)
    {
        if (pc?.SBPawn is not { } pawn)
        {
            return SmokeResult.Running;
        }

        Plan next = _plans[_plan + 1];
        double waited = context.ElapsedSeconds - _phaseStart;
        if (!_placeQueued && waited > 1.0)
        {
            // Left on a GE by the last station (the recorder's leaveGE): every GE state lets go on the crouch key.
            if (!TestRange.Walking(pc))
            {
                _keys!.Tap("bCrouch");
                _phaseStart = context.ElapsedSeconds;
                return context.ElapsedSeconds > 900 ? Finish(SmokeResult.Fail($"the spy stayed in {pc.GetStateName()}")) : SmokeResult.Running;
            }

            if (pawn.bIsCrouched)
            {
                _keys!.Tap("bCrouch");
                _phaseStart = context.ElapsedSeconds;
                return SmokeResult.Running;
            }

            if (pc.bFastSpeed != next.Fast)
            {
                _keys!.Tap("ChangeSpeed");
            }

            TestRange.Place(session, _ => session.ServerController, next.Spot, next.Yaw);
            TestRange.Face(pc, next.Yaw);
            _placeQueued = true;
            _phaseStart = context.ElapsedSeconds;
            return SmokeResult.Running;
        }

        if (_placeQueued && waited > SettleSeconds)
        {
            _plan++;
            _playing = true;
            _states.Clear();
            _sensitive = 0;
            _reinitsAtStart = _net!.Reinits;
            _appliedAtStart = _net.Applied(pc);
            _lastApplied = _appliedAtStart;
            _keys!.Play(_plans[_plan].Steps);
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"SVM_RANGE {_plans[_plan].Station}: start at {pawn.Location} in {pc.GetStateName()}"));
        }

        return context.ElapsedSeconds > 900 ? Finish(SmokeResult.Fail("timed out placing the spy")) : SmokeResult.Running;
    }

    private void Watch(SPlayerController? pc)
    {
        if (pc?.SBPawn is not { } pawn)
        {
            return;
        }

        string state = pc.GetStateName().ToString();
        _states.Add(state);
        long applied = _net!.Applied(pc);
        if (applied != _lastApplied)
        {
            _lastApplied = applied;
            if (pawn.Physics is not (EPhysics.PHYS_Walking or EPhysics.PHYS_Falling))
            {
                _sensitive++;
                _log.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{_plans[_plan].Station}: correction applied in {state} ({pawn.Physics}) at {pawn.Location}, step {_keys?.Step}"));
            }
        }
    }

    private void EndPhase(SPlayerController? pc)
    {
        Plan plan = _plans[_plan];
        long reinits = _net!.Reinits - _reinitsAtStart;
        long applied = _net.Applied(pc) - _appliedAtStart;
        SBasePawn? pawn = pc?.SBPawn;
        _log.Add(string.Create(CultureInfo.InvariantCulture,
            $"{plan.Station} ({plan.Scenario}): corrections applied {applied} ({_sensitive} during the move), reinits {reinits}, end {pc?.GetStateName()} at {pawn?.Location}, states {string.Join(",", _states)}"));
        if (reinits > 0)
        {
            _failures.Add($"{plan.Station}: the server reinitialised the spy");
        }

        if (_sensitive > 0)
        {
            _failures.Add($"{plan.Station}: corrections during the move");
        }

        if (pawn is null)
        {
            _failures.Add($"{plan.Station}: no pawn at the end");
        }
        else if (plan.Judge(pawn, _states) is { } why)
        {
            _failures.Add($"{plan.Station}: {why}");
        }
    }

    /// <summary>A scenario's inputs as key steps (frames at 60 Hz), played from a station's spot.</summary>
    private static Plan Load(TestRange.Station station, string relative, Func<SBasePawn, HashSet<string>, string?> judge)
    {
        string path = SmokePaths.Tracked(Path.Combine("tests", "scenarios", relative + ".scenario.json"));
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        bool fast = true;
        foreach (JsonElement s in root.GetProperty("setup").EnumerateArray())
        {
            if (s.GetProperty("op").GetString() == "fastSpeed")
            {
                fast = s.GetProperty("value").GetBoolean();
            }
        }

        var held = new HashSet<string>(StringComparer.Ordinal);
        var frames = new List<(int Frame, JsonElement Input)>();
        foreach (JsonElement input in root.GetProperty("inputs").EnumerateArray())
        {
            frames.Add((input.GetProperty("frame").GetInt32(), input.Clone()));
        }

        int duration = root.GetProperty("durationFrames").GetInt32();
        var steps = new List<KeyStep>();
        for (int i = 0; i < frames.Count; i++)
        {
            (int frame, JsonElement input) = frames[i];
            if (i == 0 && frame > 0)
            {
                steps.Add(new KeyStep("wait", frame / 60.0));
            }

            var taps = new List<string>();
            if (input.TryGetProperty("buttons", out JsonElement buttons))
            {
                foreach (JsonProperty b in buttons.EnumerateObject())
                {
                    if (b.Value.GetInt32() != 0)
                    {
                        held.Add(b.Name);
                    }
                    else
                    {
                        held.Remove(b.Name);
                    }
                }
            }

            if (input.TryGetProperty("jump", out JsonElement jump) && jump.GetBoolean())
            {
                taps.Add("Jump");
            }

            int end = i + 1 < frames.Count ? frames[i + 1].Frame : duration;
            steps.Add(new KeyStep($"f{frame}", Math.Max(1, end - frame) / 60.0) { Hold = [.. held], Tap = taps });
        }

        return new Plan(station.Id, relative, station.Spot, station.Yaw, fast, steps, judge);
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

/// <summary>
/// <c>--svm-smoke=range-motion</c>: the motion tracking lane. A merc at the shooting lane's firing line (0 mark,
/// facing down the lane) turns motion tracking on with its key (Tab, <c>SwitchHeadset 3</c>) as a player does; a
/// spy bot, whose keys this test holds on the server (forward, view along the line), walks back and forth behind the
/// panel wall at 1000, past the glass pane, the open window and the grating (station MotionPanels). Fails when the
/// view does not reach motion tracking, the client does not draw the spy, or the spy does not pass every opening.
/// For every opening it reports what the merc's window shows at the spy's screen point (mean brightness against the
/// panel beside it), with a screenshot per opening; what motion tracking shows through glass and grating is the
/// motion tracking spec's to decide, so those numbers are reported, not judged.
/// </summary>
public sealed class RangeMotionSmoke : SmokeTest
{
    public const string TestName = "range-motion";

    private const double SpawnTimeoutSeconds = 120.0;
    private const double WalkSeconds = 16.0;
    private const int TeardownFrames = 6;

    private readonly Dictionary<string, (int Frames, int Drawn, double Spot, double Beside)> _seen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _shotTaken = new(StringComparer.Ordinal);
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private TestRange.Station? _panels;
    private TestRange.Station? _lane;
    private int _phase;
    private double _phaseStart;
    private double _nextSample;
    private bool _hooked;
    private bool _faced;
    private volatile bool _walk;
    private SmokeResult _final;
    private int _teardown = -1;

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
            Dictionary<string, TestRange.Station> st = TestRange.Stations();
            _panels = st["MotionPanels"];
            _lane = st["Lane"];
            _root = TestRange.Start(context, PlaySide.Merc, bots: 1);
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
            if (!TestRange.Walking(pc))
            {
                return context.ElapsedSeconds > SpawnTimeoutSeconds ? Finish(SmokeResult.Fail("the merc was not drawn in a walking state")) : SmokeResult.Running;
            }

            _keys = KeyPilot.Attach(_root);
            HookSpy(session);
            TestRange.Place(session, _ => session.ServerController, _lane!.Spot, _lane.Yaw);
            TestRange.Place(session, SpyBot, _panels!.Vector("walkFrom"), 16384);
            _phaseStart = context.ElapsedSeconds;
            return SmokeResult.Running;
        }

        _keys.Update();
        double t = context.ElapsedSeconds - _phaseStart;
        switch (_phase)
        {
            case 0:
                if (pc is null)
                {
                    return SmokeResult.Running;
                }

                if (t > 2.0 && !_faced)
                {
                    // The server turned the pawn; the client's own view and aim follow the player's mouse, so they are
                    // turned here as a player would turn them (a merc's view follows the aim).
                    TestRange.Face(pc, _lane!.Yaw);
                    _faced = true;
                }

                if (t > 3.5)
                {
                    TestRange.Shot(_root, TestName, context.ElapsedSeconds, "lane-normal");
                    Camera3D? cam = _root.View?.Camera;
                    GD.Print(string.Create(CultureInfo.InvariantCulture,
                        $"SVM_RANGE motion merc at {pc.Pawn?.Location} yaw {pc.Rotation.Yaw} aim {pc.LocalMyDesiredRotation.Yaw}, view at {(cam is null ? "none" : UeSpace.ToUe(cam.GlobalPosition))} forward {(cam is null ? "none" : UeSpace.DirectionToUe(-cam.GlobalBasis.Z))}"));
                    _keys.Tap("SwitchHeadset 3");
                    _phase = 1;
                    _phaseStart = context.ElapsedSeconds;
                }

                return SmokeResult.Running;
            case 1:
                if (pc!.RendMap == SGoggle.REN_MotionTrackingVision && _root.View?.RendMap == SGoggle.REN_MotionTrackingVision)
                {
                    TestRange.Shot(_root, TestName, context.ElapsedSeconds, "motion-on");
                    _walk = true;
                    _phase = 2;
                    _phaseStart = context.ElapsedSeconds;
                    return SmokeResult.Running;
                }

                return t > 8.0
                    ? Finish(SmokeResult.Fail($"Tab did not turn motion tracking on (RendMap {pc.RendMap}, view {_root.View?.RendMap}, goggle {pc.Goggle?.CurrentMode}, keys {string.Join("; ", _keys.Notes)})"))
                    : SmokeResult.Running;
            default:
                if (context.ElapsedSeconds >= _nextSample)
                {
                    _nextSample = context.ElapsedSeconds + 0.2;
                    Sample(context, session, pc);
                }

                if (t < WalkSeconds)
                {
                    return SmokeResult.Running;
                }

                _walk = false;
                return Finish(Judge());
        }
    }

    private SPlayerController? SpyBot(ServerHost host)
    {
        if (_root?.Session?.Bots is not { } bots)
        {
            return null;
        }

        foreach (OpenScct.Game.AI.AiController bot in bots.Bots)
        {
            if (bot.Controller.Pawn is SPawnAttaque)
            {
                return bot.Controller;
            }
        }

        return null;
    }

    /// <summary>After the bots write their inputs (this handler is added after theirs), hold the spy's keys: walk the line.</summary>
    private void HookSpy(PlaySession session)
    {
        if (_hooked)
        {
            return;
        }

        _hooked = true;
        float y0 = _panels!.Vector("walkFrom").Y;
        float y1 = _panels.Vector("walkTo").Y;
        int heading = 16384;
        session.Host.BeforeWorldTick += host =>
        {
            if (SpyBot(host) is not { Pawn: SBasePawn pawn } spy)
            {
                return;
            }

            if (pawn.Location.Y >= y1 - 8f)
            {
                heading = 49152;
            }
            else if (pawn.Location.Y <= y0 + 8f)
            {
                heading = 16384;
            }

            spy.BT_Up = (byte)(_walk ? 1 : 0);
            spy.BT_Down = 0;
            spy.BT_Left = 0;
            spy.BT_Right = 0;
            spy.bCrouch = 0;
            spy.bUsing = 0;
            spy.bFire = 0;
            spy.bFastSpeed = false;
            spy.aTurn = 0f;
            spy.aLookUp = 0f;
            spy.Rotation = new FRotator(0, heading, 0);
        };
    }

    /// <summary>Which part of the panel wall the spy is behind, and what the merc's window shows there.</summary>
    private void Sample(SmokeContext context, PlaySession session, SPlayerController? pc)
    {
        SBasePawn? spy = null;
        foreach (SBasePawn p in session.Client.World.Level.DynamicActors<SBasePawn>())
        {
            if (p is SPawnAttaque && !p.bDeleteMe)
            {
                spy = p;
            }
        }

        if (spy is null || _root is null)
        {
            return;
        }

        string part = "post";
        foreach (JsonProperty o in _panels!.Entry.GetProperty("openings").EnumerateObject())
        {
            if (spy.Location.Y >= o.Value[0].GetSingle() + 20f && spy.Location.Y <= o.Value[1].GetSingle() - 20f)
            {
                part = o.Name;
            }
        }

        bool drawn = false;
        if (_root.Pawns is { } views)
        {
            foreach (SimPawnView v in views.Views)
            {
                drawn |= ReferenceEquals(v.Pawn, spy) && v.Visible;
            }
        }

        double spot = double.NaN;
        double beside = double.NaN;
        if (DisplayServer.GetName() != "headless" && _root.View?.Camera is { } camera && camera.IsInsideTree())
        {
            Vector3 chest = UeSpace.ToGodot(spy.Location + new FVector(0f, 0f, 20f));
            if (!camera.IsPositionBehind(chest))
            {
                Image image = _root.GetViewport().GetTexture().GetImage();
                // The camera may draw into a view of another size than the window the image is taken from.
                Vector2 size = camera.GetViewport().GetVisibleRect().Size;
                var scale = new Vector2(image.GetWidth() / size.X, image.GetHeight() / size.Y);
                Vector2 at = camera.UnprojectPosition(chest) * scale;
                spot = Brightness(image, at, 4);
                // The lintel over the openings, on the face toward the merc, above the spy.
                float face = _panels.Entry.GetProperty("panelX")[0].GetSingle() - 1f;
                float lintel = (_panels.Entry.GetProperty("openZ")[1].GetSingle() + 256f) / 2f;
                Vector3 post = UeSpace.ToGodot(new FVector(face, spy.Location.Y, lintel));
                beside = camera.IsPositionBehind(post) ? double.NaN : Brightness(image, camera.UnprojectPosition(post) * scale, 4);
            }
        }

        (int frames, int drawnCount, double sumSpot, double sumBeside) = _seen.TryGetValue(part, out var s) ? s : (0, 0, 0.0, 0.0);
        _seen[part] = (frames + 1, drawnCount + (drawn ? 1 : 0), sumSpot + (double.IsNaN(spot) ? 0 : spot), sumBeside + (double.IsNaN(beside) ? 0 : beside));
        if (part != "post" && _shotTaken.Add(part))
        {
            TestRange.Shot(_root, TestName, context.ElapsedSeconds, "behind-" + part);
        }

        _ = pc;
    }

    private static double Brightness(Image image, Vector2 at, int r)
    {
        int cx = (int)at.X;
        int cy = (int)at.Y;
        double sum = 0;
        int n = 0;
        for (int y = cy - r; y <= cy + r; y++)
        {
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x >= 0 && y >= 0 && x < image.GetWidth() && y < image.GetHeight())
                {
                    Color c = image.GetPixel(x, y);
                    sum += (0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B);
                    n++;
                }
            }
        }

        return n == 0 ? double.NaN : sum / n;
    }

    private SmokeResult Judge()
    {
        var lines = new List<string>();
        var missing = new List<string>();
        foreach (string part in new[] { "glass", "open", "grating" })
        {
            if (!_seen.TryGetValue(part, out var s) || s.Frames == 0)
            {
                missing.Add(part);
                continue;
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"{part}: {s.Frames} samples, spy drawn in {s.Drawn}, brightness at the spy {s.Spot / s.Frames:F3} against the wall above {s.Beside / s.Frames:F3}"));
            if (s.Drawn == 0)
            {
                missing.Add(part + " (spy not drawn)");
            }
        }

        string report = string.Join(" | ", lines);
        GD.Print("SVM_RANGE motion " + report);
        return missing.Count == 0 ? SmokeResult.Pass(report) : SmokeResult.Fail($"the spy did not pass behind: {string.Join(", ", missing)} || {report}");
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

/// <summary>
/// <c>--svm-smoke=range-bots</c>: play-bots on TestRange through the player's path (host thread, wall clock, HUD): a
/// spy and three server bots (a 2v2) on the open floor between the starts. The player stands still; the client must
/// draw a pawn for every bot, at least two must walk 300 uu from where they were 10 s in, and every drawn pawn that
/// moves must animate (<see cref="PlayInspector"/> animation check). The feet are measured and reported, not judged:
/// the crouched poses put the toes up to 15 uu under the floor, a known open issue (docs/ANIMATION.md).
/// </summary>
public sealed class RangeBotsSmoke : SmokeTest
{
    public const string TestName = "range-bots";

    public const int BotCount = 3;
    public const float MoveDistance = 300f;
    public const double SettleSeconds = 10.0;
    public const double MaxSeconds = 120.0;
    private const int TeardownFrames = 6;

    private readonly Dictionary<SBasePawn, FVector> _firstSeen = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<SBasePawn> _moved = new(ReferenceEqualityComparer.Instance);
    private PlayRoot? _root;
    private PlayInspector? _inspect;
    private double _start = -1;
    private double _nextShot;
    private SmokeResult _final;
    private int _teardown = -1;

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
            _root = TestRange.Start(context, PlaySide.Spy, BotCount);
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

        if (_inspect is null)
        {
            _inspect = new PlayInspector(_root, InspectChecks.Animation);
            _inspect.Attach();
            _start = context.ElapsedSeconds;
        }

        _inspect.Frame();
        if (context.ElapsedSeconds >= _nextShot)
        {
            _nextShot = context.ElapsedSeconds + 2.0;
            TestRange.Shot(_root, TestName, context.ElapsedSeconds, "bots");
        }

        SBasePawn? local = session.LocalPawn;
        int drawn = 0;
        if (_root.Pawns is { } pawns)
        {
            foreach (SimPawnView view in pawns.Views)
            {
                if (view.Pawn is not { } pawn || ReferenceEquals(pawn, local))
                {
                    continue;
                }

                drawn++;
                if (context.ElapsedSeconds - _start < SettleSeconds)
                {
                    continue;
                }

                if (!_firstSeen.TryGetValue(pawn, out FVector first))
                {
                    _firstSeen[pawn] = pawn.Location;
                }
                else if ((pawn.Location - first).SizeSquared() > MoveDistance * MoveDistance)
                {
                    _moved.Add(pawn);
                }
            }
        }

        double elapsed = context.ElapsedSeconds - _start;
        bool enough = drawn >= BotCount && _moved.Count >= 2;
        if (!enough && elapsed < MaxSeconds)
        {
            return SmokeResult.Running;
        }

        _inspect.Finish();
        string where = string.Join("; ", BotLines(session));
        string summary = string.Create(CultureInfo.InvariantCulture,
            $"bots drawn={drawn} moving={_moved.Count} after {elapsed:F1} s; {where}; inspector: {_inspect.Summary()}");
        GD.Print("SVM_RANGE bots " + summary);
        if (!enough)
        {
            return Finish(SmokeResult.Fail(summary));
        }

        return Finish(_inspect.Issues.Count == 0 ? SmokeResult.Pass(summary) : SmokeResult.Fail(_inspect.Report() + " || " + summary));
    }

    private static IEnumerable<string> BotLines(PlaySession session)
    {
        if (session.Bots is null)
        {
            yield break;
        }

        foreach (OpenScct.Game.AI.AiController bot in session.Bots.Bots)
        {
            yield return $"{bot.Controller.PlayerReplicationInfo?.PlayerName}: {bot.Status}";
        }
    }

    private SmokeResult Finish(SmokeResult result)
    {
        _final = result;
        _teardown = 0;
        _root?.QueueFree();
        return SmokeResult.Running;
    }
}
