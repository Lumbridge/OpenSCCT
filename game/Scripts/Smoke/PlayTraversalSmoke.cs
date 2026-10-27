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
using OpenScct.Net.Moves;
using OpenScct.Server;
using OpenScctGame.Play;
using OpenScctGame.Play.Session;

namespace OpenScctGame.Smoke;

/// <summary>
/// <c>--svm-smoke=play-traversal</c>: the ledge mount and the zip line on SteeE at the keys, the way a player plays
/// them offline (Play Level rules, host thread, wall clock, HUD). For each recorded scenario (tests/scenarios
/// traversal/spy-ledge-pull-up and spy-zip-line-29) the server places the spy at the scenario's spot and reinitialises
/// the client there, as a respawn does; then <see cref="KeyPilot"/> plays the scenario's inputs as key presses.
/// The server's corrections and reinits that reach the client are counted (<see cref="InspectNet"/>). Fails on a
/// reinit, on a correction during the mount or the ride, or when the spy does not get onto the container top or
/// along the line to the deck. With a window and <c>SVM_PLAY_SHOTS</c> it saves a screenshot every half second.
/// </summary>
public sealed class PlayTraversalSmoke : SmokeTest
{
    public const string TestName = "play-traversal";

    private const double SpawnTimeoutSeconds = 90.0;

    private const double SettleSeconds = 3.0;

    private const int TeardownFrames = 6;

    private sealed record Plan(string Scenario, FVector Spot, int Yaw, bool Fast, List<KeyStep> Steps);

    private readonly List<Plan> _plans = new();
    private readonly List<string> _log = new();
    private readonly List<string> _failures = new();
    private PlayRoot? _root;
    private KeyPilot? _keys;
    private InspectNet? _net;
    private int _plan = -1;
    private double _phaseStart;
    private bool _playing;
    private bool _placeQueued;
    private long _reinitsAtStart;
    private long _appliedAtStart;
    private readonly HashSet<string> _states = new(StringComparer.Ordinal);
    private int _sensitiveCorrections;
    private long _lastApplied;
    private float _bestZ = float.MinValue;
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
                _plans.Add(Load("traversal/spy-ledge-pull-up"));
                _plans.Add(Load("traversal/spy-zip-line-29"));
            }
            catch (IOException e)
            {
                return SmokeResult.Fail("scenario not readable: " + e.Message);
            }

            _root = PlayInspectSmoke.StartPlay(context, bots: 0);
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
            bool walking = pc is not null && pc.SBPawn is not null && pc.GetStateName().ToString().StartsWith("s_PlayerWalking", StringComparison.Ordinal);
            if (!walking)
            {
                return context.ElapsedSeconds > SpawnTimeoutSeconds
                    ? Finish(SmokeResult.Fail("the spy was not drawn in a walking state"))
                    : SmokeResult.Running;
            }

            _keys = KeyPilot.Attach(_root);
            _net = new InspectNet(session.Client);
            _net.TryInstall(pc);
            _phaseStart = context.ElapsedSeconds;
            return SmokeResult.Running;
        }

        _keys.Update();
        SaveShot(context, pc);
        if (!_playing)
        {
            return Place(context, session, pc);
        }

        Watch(pc);
        if (!_keys.Done)
        {
            return SmokeResult.Running;
        }

        EndPhase(session, pc);
        _playing = false;
        _placeQueued = false;
        _phaseStart = context.ElapsedSeconds;
        if (_plan + 1 >= _plans.Count)
        {
            foreach (string line in _log)
            {
                GD.Print("SVM_TRAVERSAL " + line);
            }

            GD.Print($"SVM_TRAVERSAL keys: {string.Join("; ", _keys.Notes)}; sent {_keys.Sent} delivered {_keys.Delivered}");
            return Finish(_failures.Count == 0
                ? SmokeResult.Pass(string.Join(" | ", _log))
                : SmokeResult.Fail(string.Join(" | ", _failures) + " || " + string.Join(" | ", _log)));
        }

        return SmokeResult.Running;
    }

    /// <summary>Between scenarios: the server places the spy, the stance and speed keys are set, then the inputs start.</summary>
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

            // On the server's thread, before its next world tick: place the pawn and reinitialise the client there.
            FVector at = next.Spot + new FVector(0f, 0f, pawn.CollisionHeight + 2f);
            at = new FVector(MathF.Round(at.X), MathF.Round(at.Y), MathF.Round(at.Z));
            int yaw = next.Yaw;
            Action<ServerHost>? once = null;
            once = host =>
            {
                host.BeforeWorldTick -= once;
                if (session.ServerController is { Pawn: SBasePawn serverPawn } spc &&
                    host.World.Level.GetService<OpenScct.Game.Movement.IMovementNet>() is NetMovementBridge bridge &&
                    bridge.EndpointOf(spc)?.Server is { } moves)
                {
                    MovementNatives.FarMoveActor(serverPawn, at);
                    serverPawn.Velocity = default;
                    serverPawn.Acceleration = default;
                    serverPawn.Rotation = new FRotator(0, yaw, 0);
                    moves.ReinitPawn();
                }
            };
            session.Host.BeforeWorldTick += once;
            pc.Rotation = new FRotator(0, yaw, 0);
            _placeQueued = true;
            _phaseStart = context.ElapsedSeconds;
            return SmokeResult.Running;
        }

        if (_placeQueued && waited > SettleSeconds)
        {
            _plan++;
            _playing = true;
            _states.Clear();
            _sensitiveCorrections = 0;
            _bestZ = float.MinValue;
            _reinitsAtStart = _net!.Reinits;
            _appliedAtStart = _net.Applied(pc);
            _lastApplied = _appliedAtStart;
            _keys!.Play(_plans[_plan].Steps);
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"SVM_TRAVERSAL {_plans[_plan].Scenario}: start at {pawn.Location} in {pc.GetStateName()}"));
        }

        return context.ElapsedSeconds > 600 ? Finish(SmokeResult.Fail("timed out placing the spy")) : SmokeResult.Running;
    }

    /// <summary>Each frame of a scenario: the states the client went through and the corrections during the mount or the ride.</summary>
    private void Watch(SPlayerController? pc)
    {
        if (pc?.SBPawn is not { } pawn)
        {
            return;
        }

        string state = pc.GetStateName().ToString();
        _states.Add(state);
        _bestZ = Math.Max(_bestZ, pawn.Location.Z);
        long applied = _net!.Applied(pc);
        if (applied != _lastApplied)
        {
            _lastApplied = applied;
            if (pawn.Physics is EPhysics.PHYS_RootMotion or EPhysics.PHYS_Linear || state.StartsWith("s_Zip", StringComparison.Ordinal) ||
                state.StartsWith("TopLedge", StringComparison.Ordinal))
            {
                _sensitiveCorrections++;
            }
        }
    }

    private void EndPhase(PlaySession session, SPlayerController? pc)
    {
        Plan plan = _plans[_plan];
        long reinits = _net!.Reinits - _reinitsAtStart;
        long applied = _net.Applied(pc) - _appliedAtStart;
        SBasePawn? pawn = pc?.SBPawn;
        string line = string.Create(CultureInfo.InvariantCulture,
            $"{plan.Scenario}: corrections applied {applied} ({_sensitiveCorrections} in the mount or ride), reinits {reinits}, end {pc?.GetStateName()} at {pawn?.Location}, states {string.Join(",", _states)}");
        _log.Add(line);
        if (reinits > 0)
        {
            _failures.Add($"{plan.Scenario}: the server reinitialised the spy");
        }

        if (_sensitiveCorrections > 0)
        {
            _failures.Add($"{plan.Scenario}: corrections during the mount or the ride");
        }

        if (plan.Scenario.EndsWith("pull-up", StringComparison.Ordinal) && !(_states.Contains("TopLedge") && pawn is { Location.Z: > -880f }))
        {
            _failures.Add($"{plan.Scenario}: the spy did not climb onto the container");
        }

        if (plan.Scenario.EndsWith("zip-line-29", StringComparison.Ordinal) && !(_states.Contains("s_ZipLine") && _states.Contains("OnLedge")))
        {
            _failures.Add($"{plan.Scenario}: the spy did not ride the line and catch the deck ledge");
        }

        _ = session;
    }

    /// <summary>A scenario's spot and inputs, the inputs as key steps (frames of the scenario at 60 Hz).</summary>
    private static Plan Load(string relative)
    {
        string path = SmokePaths.Tracked(Path.Combine("tests", "scenarios", relative + ".scenario.json"));
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;
        JsonElement floor = root.GetProperty("spawn").GetProperty("floor");
        var spot = new FVector(floor[0].GetSingle(), floor[1].GetSingle(), floor[2].GetSingle());
        int yaw = root.GetProperty("spawn").GetProperty("yaw").GetInt32();
        bool fast = true;
        foreach (JsonElement s in root.GetProperty("setup").EnumerateArray())
        {
            if (s.GetProperty("op").GetString() == "fastSpeed")
            {
                fast = s.GetProperty("value").GetBoolean();
            }
        }

        // Each input frame starts a step that lasts until the next one; buttons stay held until a later input lifts them.
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

        return new Plan(relative, spot, yaw, fast, steps);
    }

    private void SaveShot(SmokeContext context, SPlayerController? pc)
    {
        string? folder = System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS");
        if (string.IsNullOrWhiteSpace(folder) || DisplayServer.GetName() == "headless" || _root is null || context.ElapsedSeconds < _nextShot)
        {
            return;
        }

        _nextShot = context.ElapsedSeconds + 0.5;
        Directory.CreateDirectory(folder);
        string label = _playing && _plan >= 0 ? Path.GetFileName(_plans[_plan].Scenario) + "-" + (pc?.GetStateName().ToString() ?? "") : "setup";
        Image image = _root.GetViewport().GetTexture().GetImage();
        image.SavePng(Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{TestName}-{context.ElapsedSeconds:000.0}-{label}.png")));
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
