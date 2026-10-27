using System;
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

/// <summary>
/// A running spy hit by merc fire, as the local player of an offline match (host and connected client): the hits
/// reach the client as PlayAnimDmg, which starts the adrenaline (spec/combat/damage-pipeline.md rule 39) and the
/// damage clips. The run must stay smooth: no correction pulls the client's spy back while it runs. The original's
/// move packing put the spy's adrenaline flag in the zone bits, so its server kept the 423.5 cap while the client
/// ran at 508 uu/s, and corrected it about 25 uu every 0.3 s (spec/native/net-moves.md Edge cases).
/// </summary>
public sealed class SpyHitRunNetTests
{
    private readonly ITestOutputHelper _output;

    public SpyHitRunNetTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void A_running_spy_under_fire_is_not_pulled_back()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("TestRange") is null, "The TestRange map is not in this checkout.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "TestRange",
            Side = PlaySide.Spy,
            Rules = PlayRules.PlayLevel,
            HostMode = PlayHostMode.Inline,
            MatchSeed = 3,
        }, clock);
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

        ClientMoves clientMoves = ((NetMovementBridge)session.Client.World.Level.GetService<IMovementNet>()!).EndpointOf(client)!.Client!;
        if (((SBasePawn)client.Pawn!).bIsCrouched)
        {
            session.Input.Set("bCrouch", true);
            for (int i = 0; i < 3; i++)
            {
                Tick();
            }

            session.Input.Set("bCrouch", false);
            for (int i = 0; i < 90; i++)
            {
                Tick();
            }
        }

        // Facing +X from the spawn the range is open for the whole run; full speed (the run toggle).
        client.Rotation = new FRotator(0, 0, 0);
        client.bFastSpeed = true;

        session.Input.Set("BT_Up", true);
        var log = new StringBuilder();
        float largest = 0f;
        float clientSpeed = 0f;
        float serverSpeed = 0f;
        int applied = 0;
        for (int t = 0; t < 4 * Sim.Hz; t++)
        {
            if (t >= Sim.Hz / 2 && t % 20 == 0 && server.Pawn is { Health: > 20 } spy)
            {
                // A rifle round in the back (pill 2), as a merc's fire delivers it.
                spy.Health = 100;
                server.PawnTakeDamage(5, null, spy.Location, spy.Rotation.Vector() * 100f, SDamageType.SDTBase, 2, 70);
            }

            int before = clientMoves.CorrectionsApplied;
            Tick();
            if (t == 3 * Sim.Hz)
            {
                clientSpeed = client.Pawn!.Velocity.Size2D();
                serverSpeed = server.Pawn!.Velocity.Size2D();
            }

            if (clientMoves.CorrectionsApplied != before && clientMoves.UpdatingSMove is { } matched)
            {
                float size = (clientMoves.vUpdateLocation - matched.SavedLocation).Size();
                largest = Math.Max(largest, size);
                applied++;
                log.Append(CultureInfo.InvariantCulture, $"[t{t} {size:F1} uu speed {client.Pawn!.Velocity.Size2D():F0} state {client.GetStateName()}] ");
            }
        }

        session.Input.Set("BT_Up", false);
        _output.WriteLine($"applied {applied}, largest {largest:F1}: {log}");
        Assert.True(client.fTimeWhenHit > 0f, "the hits never reached the client");

        // Adrenaline (1.2) on both sides: 423.5 x 1.2 = 508 uu/s.
        Assert.InRange(clientSpeed, 500f, 515f);
        Assert.InRange(serverSpeed, 500f, 515f);
        Assert.True(largest <= 16f, "a correction pulled the running spy back: " + log);
    }
}
