using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>
/// Traversal the map sweep found broken on converted maps (docs/MAPS.md), played as the local player of an offline
/// match (inline host, loopback) with the input bytes the bound keys give. The player is put at the place as a
/// respawn puts him (the server moves the pawn and reinitialises the client), then holds keys.
/// </summary>
public sealed class MapSweepTraversalTests
{
    private readonly ITestOutputHelper _output;

    public MapSweepTraversalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// ClubE's ladder (GE54, x 2074, y 887, from z 944 to 1392) from its foot, facing it. The spy spawns in the slow
    /// speed mode, where a held key gives a stick of 2400, under the 3000 the walking modes need for up and down; on a
    /// ladder a spy's up and down come from the stick's direction alone (NativeComputeControlVariables normalises it),
    /// so the forward key climbs at either speed. The slow spy used to grab the ladder and hang at its foot.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_spy_climbs_the_ClubE_ladder_with_the_forward_key(bool fast)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("ClubE") is null, "ClubE has not been converted into .content/svmap.");
        using var run = new PlacedPlayer("ClubE", PlaySide.Spy);
        if (fast)
        {
            run.Tap("ChangeSpeed");
        }

        run.Place(new FVector(2140f, 887f, 1036f), 32768);
        float startZ = run.Client.Location.Z;
        var states = new List<string>();
        run.Hold(["BT_Up"], 4.0, () =>
        {
            string s = run.ClientController.GetStateName().ToString();
            if (states.Count == 0 || states[^1] != s)
            {
                states.Add(s);
            }
        });

        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"fast {fast}: client z {startZ:F0} -> {run.Client.Location.Z:F0}, server z {run.Server.Location.Z:F0}, states {string.Join(" > ", states)}"));
        Assert.Contains("s_OnLadder", states);

        // 126 uu/s for about 3 s on the ladder after the grab.
        Assert.True(run.Client.Location.Z - startZ > 250f, $"the spy climbed {run.Client.Location.Z - startZ:F0} uu ({string.Join(" > ", states)})");
        Assert.True(MathF.Abs(run.Server.Location.Z - run.Client.Location.Z) < 40f, "the server's pawn climbed with the client's");
    }
}

/// <summary>
/// The local player of an offline match (inline host, loopback, Play Level rules, no bots), stepped on a step clock,
/// put at a place by the server as a respawn does, with keys held through the session's input bytes.
/// </summary>
internal sealed class PlacedPlayer : IDisposable
{
    private readonly StepClock _clock = new();

    public PlacedPlayer(string map, PlaySide side)
    {
        Session = new PlaySession(new PlaySettings
        {
            Map = map,
            Side = side,
            // The mode the host menu preselects (a map code's last letter is its version, not its mode).
            Mode = OpenScct.Server.MapCatalog.ModeKey(OpenScct.Server.MapCatalog.CreateDefault().GetCatalogEntry(map)?.DefaultMode ?? OpenScct.Core.EGameMode.GM_Multi),
            Rules = PlayRules.PlayLevel,
            HostMode = PlayHostMode.Inline,
        }, _clock);
        for (int i = 0; i < 1200 && (Session.LocalPawn is null || Session.ServerController?.Pawn is null ||
            Session.LocalController!.IsInState("WaitingForPawn")); i++)
        {
            Tick();
        }

        if (Session.LocalPawn is null || Session.ServerController?.Pawn is null)
        {
            throw new InvalidOperationException("the local player never got a pawn");
        }

        if (side == PlaySide.Spy)
        {
            // The spy spawns crouched: stand up.
            Tap("bCrouch");
            Ticks(90);
        }
    }

    public PlaySession Session { get; }

    public SPlayerController ClientController => Session.LocalController!;

    public SBasePawn Client => Session.LocalPawn!;

    public SBasePawn Server => (SBasePawn)Session.ServerController!.Pawn!;

    public void Tick()
    {
        _clock.NowMicroseconds += 1_000_000 / Sim.Hz;
        Session.Step(_clock.NowMicroseconds);
        Session.Input.EndTick();
    }

    public void Ticks(int n)
    {
        for (int i = 0; i < n; i++)
        {
            Tick();
        }
    }

    /// <summary>Presses and releases a button (or runs an exec command), then lets a few ticks pass.</summary>
    public void Tap(string action)
    {
        if (Session.Input.Set(action, true))
        {
            Tick();
            Session.Input.Set(action, false);
        }
        else
        {
            Session.Input.Commands.Add(new InputCommand(action));
            Tick();
        }

        Ticks(10);
    }

    /// <summary>Puts the pawn at <paramref name="at"/> facing <paramref name="yaw"/> on the server and reinitialises the client there.</summary>
    public void Place(FVector at, int yaw)
    {
        SPlayerController server = Session.ServerController!;
        SBasePawn pawn = Server;
        if (Session.Host.World.Level.GetService<IMovementNet>() is not NetMovementBridge bridge || bridge.EndpointOf(server)?.Server is not { } moves)
        {
            throw new InvalidOperationException("no move endpoint for the local player");
        }

        MovementNatives.FarMoveActor(pawn, at);
        pawn.Velocity = default;
        pawn.Acceleration = default;
        pawn.Rotation = new FRotator(0, yaw, 0);
        server.Rotation = pawn.Rotation;
        moves.ReinitPawn();
        Ticks(30);
        ClientController.SetViewRotation(new FRotator(0, yaw, 0));
        Ticks(2);
    }

    /// <summary>Holds <paramref name="buttons"/> for <paramref name="seconds"/>, calling <paramref name="each"/> after every tick.</summary>
    public void Hold(IReadOnlyList<string> buttons, double seconds, Action? each = null)
    {
        foreach (string b in buttons)
        {
            Session.Input.Set(b, true);
        }

        int ticks = (int)Math.Round(seconds * Sim.Hz);
        for (int i = 0; i < ticks; i++)
        {
            Tick();
            each?.Invoke();
        }

        foreach (string b in buttons)
        {
            Session.Input.Set(b, false);
        }
    }

    public void Dispose() => Session.Dispose();
}
