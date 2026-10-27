using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Moves;
using OpenScct.Server;
using OpenScctGame.Play.Session;

namespace OpenScct.Integration.Tests;

/// <summary>A fixed-step clock the play session's inline host and client read.</summary>
internal sealed class StepClock : IClock
{
    public long NowMicroseconds { get; set; }
}

/// <summary>One applied correction as the local player sees it.</summary>
internal readonly record struct RubberCorrection(int Frame, float Size, EPhysics Physics, Name State);

/// <summary>What the local player saw in one tick of a <see cref="RubberBandRun"/>.</summary>
internal readonly record struct RubberSample(
    int Frame, FVector Client, FVector Server, EPhysics ClientPhysics, EPhysics ServerPhysics, Name ClientState, Name ServerState);

/// <summary>
/// A recorded traversal scenario (tests/scenarios, svm.scenario/1) played over the network the way a player plays
/// offline: a <see cref="PlaySession"/> hosts a listen server and joins it with the local client (loopback, or a
/// SimLink profile), and the scenario's inputs go into <see cref="PlaySession.Input"/> as the input layer fills it from
/// the bound keys (input bytes, the Jump command with its aUp axis, ChangeSpeed), so they travel through the client
/// controller and the move protocol to the server. The pawn is placed at the scenario's spot by the server, which
/// reinitialises the client there, as a respawn would. The run records the corrections the client applied, the
/// server's reinits and both copies of the pawn every tick.
/// </summary>
internal sealed class RubberBandRun
{
    /// <summary>Ticks to wait after the placement before the scenario's inputs start.</summary>
    public const int SettleTicks = 180;

    private readonly List<RubberSample> _samples = new();
    private readonly List<RubberCorrection> _corrections = new();
    private readonly List<int> _reinits = new();

    private RubberBandRun(string scenario)
    {
        Scenario = scenario;
    }

    public string Scenario { get; }

    public IReadOnlyList<RubberSample> Samples => _samples;

    /// <summary>Corrections the client applied during the scenario's frames (not the placement).</summary>
    public IReadOnlyList<RubberCorrection> Corrections => _corrections;

    /// <summary>Frames at which the server reinitialised the pawn (ReinitPawnBecauseOfDivergences).</summary>
    public IReadOnlyList<int> Reinits => _reinits;

    /// <summary>Moves the server ignored during the scenario (physics mismatch, stale stamps).</summary>
    public long MovesIgnored { get; private set; }

    public RubberSample Last => _samples[^1];

    /// <summary>The repository root (the folder that holds OpenSCCT.slnx).</summary>
    public static string RepoRoot { get; } = FindRoot();

    public static string ScenarioPath(string relative) => Path.Combine(RepoRoot, "tests", "scenarios", relative + ".scenario.json");

    /// <summary>The map the scenario plays on, read from its file.</summary>
    public static string MapOf(string relative)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(ScenarioPath(relative)));
        return doc.RootElement.GetProperty("map").GetString()!;
    }

    /// <summary>Plays <paramref name="relative"/> (for example <c>traversal/spy-zip-line-29</c>) over <paramref name="link"/> (null: loopback).</summary>
    public static RubberBandRun Play(string relative, SimLinkProfile? link, ulong seed = 1)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(ScenarioPath(relative)));
        JsonElement root = doc.RootElement;
        string team = root.GetProperty("players")[0].GetProperty("team").GetString()!;
        JsonElement spawn = root.GetProperty("spawn");
        JsonElement floor = spawn.GetProperty("floor");
        var spot = new FVector(floor[0].GetSingle(), floor[1].GetSingle(), floor[2].GetSingle());
        float clearance = spawn.GetProperty("clearance").GetSingle();
        int yaw = spawn.GetProperty("yaw").GetInt32();
        bool crouched = false;
        bool fast = true;
        foreach (JsonElement step in root.GetProperty("setup").EnumerateArray())
        {
            switch (step.GetProperty("op").GetString())
            {
                case "stance":
                    crouched = step.GetProperty("crouched").GetBoolean();
                    break;
                case "fastSpeed":
                    fast = step.GetProperty("value").GetBoolean();
                    break;
                case "view":
                    yaw = step.GetProperty("yaw").GetInt32();
                    break;
            }
        }

        var inputs = new Dictionary<int, JsonElement>();
        foreach (JsonElement input in root.GetProperty("inputs").EnumerateArray())
        {
            inputs[input.GetProperty("frame").GetInt32()] = input.Clone();
        }

        int frames = root.GetProperty("durationFrames").GetInt32() + 60;
        var run = new RubberBandRun(relative);
        var clock = new StepClock();
        var settings = new PlaySettings
        {
            Map = root.GetProperty("map").GetString()!,
            Side = team == "merc" ? PlaySide.Merc : PlaySide.Spy,
            Rules = PlayRules.PlayLevel,
            HostMode = PlayHostMode.Inline,
            SimLink = link,
            MatchSeed = seed,
        };
        using var session = new PlaySession(settings, clock);
        void Tick()
        {
            clock.NowMicroseconds += 1_000_000 / Sim.Hz;
            session.Step(clock.NowMicroseconds);
            session.Input.Up = 0f;
        }

        for (int i = 0; i < 1200 && (session.LocalPawn is null || session.ServerController?.Pawn is null ||
            session.LocalController!.IsInState("WaitingForPawn")); i++)
        {
            Tick();
        }

        SPlayerController client = session.LocalController ?? throw new InvalidOperationException("the local player never got a pawn");
        SPlayerController server = session.ServerController!;
        for (int i = 0; i < 120; i++)
        {
            Tick();
        }

        // Stance and speed mode as the keys set them.
        var pawn = (SBasePawn)client.Pawn!;
        if (client.bFastSpeed != fast)
        {
            session.Input.Commands.Add(new InputCommand("ChangeSpeed"));
        }

        if (pawn.bIsCrouched != crouched)
        {
            session.Input.Set("bCrouch", true);
            Tick();
            session.Input.Set("bCrouch", false);
        }

        for (int i = 0; i < 90; i++)
        {
            Tick();
        }

        // The server places its pawn at the spot and reinitialises the client there.
        var serverPawn = (SBasePawn)server.Pawn!;
        FVector at = spot + new FVector(0f, 0f, pawn.CollisionHeight + clearance);
        at = new FVector(MathF.Round(at.X), MathF.Round(at.Y), MathF.Round(at.Z));
        MovementNatives.FarMoveActor(serverPawn, at);
        serverPawn.Velocity = default;
        serverPawn.Acceleration = default;
        serverPawn.Rotation = new FRotator(0, yaw, 0);
        ServerMoves serverMoves = Endpoint(session.Host.World.Level, server).Server!;
        serverMoves.ReinitPawn();
        client.Rotation = new FRotator(0, yaw, 0);
        for (int i = 0; i < SettleTicks; i++)
        {
            Tick();
        }

        ClientMoves clientMoves = Endpoint(session.Client.World.Level, client).Client!;
        long reinits = serverMoves.Sent.Reinits;
        long ignored = serverMoves.MovesIgnored;
        for (int f = 0; f < frames; f++)
        {
            if (inputs.TryGetValue(f, out JsonElement input))
            {
                if (input.TryGetProperty("buttons", out JsonElement buttons))
                {
                    foreach (JsonProperty b in buttons.EnumerateObject())
                    {
                        session.Input.Set(b.Name, b.Value.GetInt32() != 0);
                    }
                }

                // IK_Space=Function Jump | Axis aUp Speed=+280.0: the command on the press, the axis for the tick.
                if (input.TryGetProperty("jump", out JsonElement jump) && jump.GetBoolean())
                {
                    session.Input.Commands.Add(new InputCommand("Jump"));
                    session.Input.Up = 280f;
                }

                if (input.TryGetProperty("view", out JsonElement view))
                {
                    client.Rotation = new FRotator(view.GetProperty("pitch").GetInt32(), view.GetProperty("yaw").GetInt32(), 0);
                }
            }

            int applied = clientMoves.CorrectionsApplied;
            Tick();
            var cp = (SBasePawn)client.Pawn!;
            var sp = (SBasePawn)server.Pawn!;
            if (clientMoves.CorrectionsApplied != applied && clientMoves.UpdatingSMove is { } matched)
            {
                run._corrections.Add(new RubberCorrection(f, (clientMoves.vUpdateLocation - matched.SavedLocation).Size(), cp.Physics,
                    client.GetStateName()));
            }

            if (serverMoves.Sent.Reinits != reinits)
            {
                reinits = serverMoves.Sent.Reinits;
                run._reinits.Add(f);
            }

            run._samples.Add(new RubberSample(f, cp.Location, sp.Location, cp.Physics, sp.Physics, client.GetStateName(), server.GetStateName()));
        }

        run.MovesIgnored = serverMoves.MovesIgnored - ignored;
        return run;
    }

    private static MoveEndpoint Endpoint(Level level, SPlayerController pc) =>
        ((NetMovementBridge)level.GetService<IMovementNet>()!).EndpointOf(pc)!;

    /// <summary>The first frame the client's pawn is in <paramref name="physics"/>, or -1.</summary>
    public int FirstFrame(EPhysics physics)
    {
        foreach (RubberSample s in _samples)
        {
            if (s.ClientPhysics == physics)
            {
                return s.Frame;
            }
        }

        return -1;
    }

    /// <summary>The first frame the client's controller is in <paramref name="state"/>, or -1.</summary>
    public int FirstFrame(string state)
    {
        foreach (RubberSample s in _samples)
        {
            if (s.ClientState.ToString() == state)
            {
                return s.Frame;
            }
        }

        return -1;
    }

    /// <summary>The corrections applied in frames <paramref name="from"/> to <paramref name="to"/> (inclusive).</summary>
    public List<RubberCorrection> CorrectionsBetween(int from, int to) =>
        _corrections.FindAll(c => c.Frame >= from && c.Frame <= to);

    /// <summary>The largest correction applied, in uu.</summary>
    public float LargestCorrection()
    {
        float largest = 0f;
        foreach (RubberCorrection c in _corrections)
        {
            largest = Math.Max(largest, c.Size);
        }

        return largest;
    }

    /// <summary>The distance between the client's and the server's pawn at the end.</summary>
    public float FinalError => (Last.Client - Last.Server).Size();

    /// <summary>A short account of the run for assertion messages.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{Scenario}: reinits at [{string.Join(",", _reinits)}], ignored moves {MovesIgnored}, corrections ");
        foreach (RubberCorrection c in _corrections)
        {
            sb.Append(CultureInfo.InvariantCulture, $"f{c.Frame} {c.Size:F1} uu in {c.State}/{c.Physics}; ");
        }

        RubberSample last = Last;
        sb.Append(CultureInfo.InvariantCulture,
            $"end client {last.ClientState}/{last.ClientPhysics} at {last.Client}, server {last.ServerState}/{last.ServerPhysics} at {last.Server}");
        return sb.ToString();
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OpenSCCT.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("OpenSCCT.slnx not found above " + AppContext.BaseDirectory);
    }
}
