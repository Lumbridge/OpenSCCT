using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Net.Moves;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>One step of <see cref="PlayStepRun"/>: buttons held, commands tapped on its first tick, mouse counts per tick.</summary>
internal sealed record PlayStep(string Name, double Seconds)
{
    public string[] Hold { get; init; } = [];

    public string[] Tap { get; init; } = [];

    public float MouseXPerTick { get; init; }
}

/// <summary>An applied correction of a <see cref="PlayStepRun"/>, with the step it fell in.</summary>
internal readonly record struct StepCorrection(int Tick, string Step, int TickInStep, float Size, EPhysics Physics, Name State, float Speed);

/// <summary>
/// The walking part of the play-inspect smoke's script (walk, turn, run, strafe, crouch walk, jump) played as the
/// local player of an offline match (<see cref="PlaySession"/>, inline host, loopback), from the map's spawn, with the
/// inputs as the input layer fills them from the bound keys. Records every correction the client applied and every
/// correction the server sent.
/// </summary>
internal sealed class PlayStepRun
{
    private readonly List<StepCorrection> _applied = new();

    public IReadOnlyList<StepCorrection> Applied => _applied;

    public long Sent { get; private set; }

    /// <summary>The largest correction the client applied, in uu.</summary>
    public float LargestApplied
    {
        get
        {
            float largest = 0f;
            foreach (StepCorrection c in _applied)
            {
                largest = Math.Max(largest, c.Size);
            }

            return largest;
        }
    }

    public long Reinits { get; private set; }

    public double Seconds { get; private set; }

    public static IReadOnlyList<PlayStep> InspectScript { get; } =
    [
        new("stand", 1.0),
        new("walk", 4.0) { Hold = ["BT_Up"] },
        new("look around", 2.5) { MouseXPerTick = 6f },
        new("walk back", 3.0) { Hold = ["BT_Down"] },
        new("fast speed", 0.4) { Tap = ["ChangeSpeed"] },
        new("run", 3.0) { Hold = ["BT_Up"] },
        new("run back", 2.5) { Hold = ["BT_Down"] },
        new("strafe left", 1.5) { Hold = ["BT_Left"] },
        new("strafe right", 1.5) { Hold = ["BT_Right"] },
        new("slow speed", 0.4) { Tap = ["ChangeSpeed"] },
        new("walk turning", 2.5) { Hold = ["BT_Up"], MouseXPerTick = 4f },
        new("crouch", 1.2) { Tap = ["bCrouch"] },
        new("crouch walk", 4.0) { Hold = ["BT_Up"], MouseXPerTick = 2f },
        new("crouch still", 1.5),
        new("stand up", 1.6) { Tap = ["bCrouch"] },
        new("jump", 1.8) { Tap = ["Jump"] },
        new("jump forward", 2.2) { Hold = ["BT_Up"], Tap = ["Jump"] },
        new("still", 2.0),
    ];

    public static PlayStepRun Play(string map, PlaySide side, IReadOnlyList<PlayStep> steps, SimLinkProfile? link = null, ulong seed = 1)
    {
        var clock = new StepClock();
        var settings = new PlaySettings
        {
            Map = map,
            Side = side,
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
            session.Input.EndTick();
        }

        for (int i = 0; i < 1200 && (session.LocalPawn is null || session.ServerController?.Pawn is null ||
            session.LocalController!.IsInState("WaitingForPawn")); i++)
        {
            Tick();
        }

        SPlayerController client = session.LocalController ?? throw new InvalidOperationException("the local player never got a pawn");
        SPlayerController server = session.ServerController!;
        for (int i = 0; i < 240; i++)
        {
            Tick();
        }

        ClientMoves clientMoves = Endpoint(session.Client.World.Level, client).Client!;
        ServerMoves serverMoves = Endpoint(session.Host.World.Level, server).Server!;
        long sent0 = serverMoves.Sent.Corrections + serverMoves.Sent.ShortCorrections;
        long reinit0 = serverMoves.Sent.Reinits;
        var run = new PlayStepRun();
        int tick = 0;
        foreach (PlayStep step in steps)
        {
            int ticks = (int)Math.Round(step.Seconds * Sim.Hz);
            foreach (string b in step.Hold)
            {
                session.Input.Set(b, true);
            }

            for (int t = 0; t < ticks; t++, tick++)
            {
                if (t == 0)
                {
                    foreach (string c in step.Tap)
                    {
                        if (!session.Input.Set(c, true))
                        {
                            session.Input.Commands.Add(new InputCommand(c));
                            if (c == "Jump")
                            {
                                session.Input.Up = 280f;
                            }
                        }
                    }
                }
                else if (t == 3)
                {
                    foreach (string c in step.Tap)
                    {
                        session.Input.Set(c, false);
                    }

                    session.Input.Up = 0f;
                }

                session.Input.MouseX = step.MouseXPerTick;
                int applied = clientMoves.CorrectionsApplied;
                Tick();
                var cp = (SBasePawn)client.Pawn!;
                if (clientMoves.CorrectionsApplied != applied && clientMoves.UpdatingSMove is { } matched)
                {
                    run._applied.Add(new StepCorrection(tick, step.Name, t, (clientMoves.vUpdateLocation - matched.SavedLocation).Size(),
                        cp.Physics, client.GetStateName(), cp.Velocity.Size2D()));
                }
            }

            foreach (string b in step.Hold)
            {
                session.Input.Set(b, false);
            }
        }

        run.Sent = serverMoves.Sent.Corrections + serverMoves.Sent.ShortCorrections - sent0;
        run.Reinits = serverMoves.Sent.Reinits - reinit0;
        run.Seconds = (double)tick / Sim.Hz;
        return run;
    }

    private static MoveEndpoint Endpoint(Level level, SPlayerController pc) =>
        ((NetMovementBridge)level.GetService<IMovementNet>()!).EndpointOf(pc)!;

    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{Seconds:F1} s, sent {Sent}, reinits {Reinits}, applied {_applied.Count}: ");
        foreach (StepCorrection c in _applied)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"[{c.Step} +{c.TickInStep} {c.Size:F1} uu {c.State}/{c.Physics} speed {c.Speed:F0}] ");
        }

        return sb.ToString();
    }
}

/// <summary>
/// Corrections in ordinary play (walk, run, strafe, crouch walk, jumps) as the play-inspect smoke plays it on SteeE,
/// from the spawn, where the spy ends up walking and jumping along the wall at x = -1664. The server sends a
/// correction at least once a second while a client sends moves (net-moves.md rule 12, the forced update); the client
/// applies only those that disagree with its saved move by more than fErrorMax, and the decoded protocol makes a few
/// small ones (docs/NETCODE.md). The server's copy of the spy sits on the rounded location of the big moves, exactly
/// on the wall's plane; before the box contact skin (docs/PHYSICS.md) its wall probes stopped it there and a box
/// sweep met the slope behind the wall, so the jump along the wall ended in a 110 uu pull down at the landing.
/// </summary>
public sealed class PlayStepCorrectionTests
{
    /// <summary>The largest correction the decoded protocol makes in ordinary play (a frame of running, 7 uu, plus margin).</summary>
    private const float SmallCorrection = 16f;

    private readonly ITestOutputHelper _output;

    public PlayStepCorrectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(PlaySide.Spy)]
    [InlineData(PlaySide.Merc)]
    public void OrdinaryPlayAlongAWallIsNotPulledBack(PlaySide side)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("SteeE") is null, "SteeE has not been converted into .content/svmap.");
        PlayStepRun run = PlayStepRun.Play("SteeE", side, PlayStepRun.InspectScript);
        _output.WriteLine(run.Describe());
        Assert.Equal(0, run.Reinits);
        Assert.True(run.LargestApplied <= SmallCorrection, "a correction pulled the player back: " + run.Describe());
    }
}
