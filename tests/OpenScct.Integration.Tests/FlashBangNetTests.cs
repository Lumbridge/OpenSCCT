using System;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>
/// spec/equipment/flash-bang.md over the wire: a flash-bang that goes off on the host in front of a connected merc
/// client flashes that client (the SDTFlash ClientDamage reaches his machine and gives his HUD the flash), and the
/// SFlashBang flare replicates and lights the spot there. Skipped without the converted SteeE.
/// </summary>
public sealed class FlashBangNetTests
{
    [Fact]
    public void A_flash_bang_on_the_host_flashes_the_connected_merc_and_its_flare_reaches_him()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("SteeE") is null, "SteeE has not been converted into .content/svmap.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "SteeE",
            Side = PlaySide.Merc,
            Rules = PlayRules.PlayLevel,
            HostMode = PlayHostMode.Inline,
            MatchSeed = 7,
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
        for (int i = 0; i < 120; i++)
        {
            Tick();
        }

        Assert.Equal(PawnType.SPAWNDEFENSE, server.SPawnType);
        Pawn merc = server.Pawn!;
        FVector eye = merc.Location + new FVector(0f, 0f, merc.EyeHeight);
        FVector at = eye + (merc.Rotation.Vector() * 40f);
        SProjFlashBang flash = server.XLevel.SpawnActor(new SProjFlashBang(), at, default)!;
        flash.Explode(at, FVector.Zero);

        int flashed = 0;
        bool flare = false;
        bool light = false;
        void Count(ReadOnlySpan<SimEvent> events)
        {
            foreach (SimEvent e in events)
            {
                if (e.Kind == SimEventKind.Notify && e.What == CombatFx.ClientDamageHudEffect && e.ActorId == client.ActorId
                    && e.Slot == (int)SDamageType.SDTFlash)
                {
                    flashed++;
                }
            }
        }

        // The ClientDamage call lands before the client's step, outside any tick: the play view hands those late
        // events to the HUD just before the step (PlayRoot, HudEventTap.TakeLate), as done here.
        void Late(ClientSession c) => Count(c.World.Events.Late);
        session.Client.BeforeWorldTick += Late;
        for (int i = 0; i < Sim.Hz && !(flashed > 0 && flare && light); i++)
        {
            Tick();
            Count(session.Client.World.Events.Current);
            foreach (SFlashBang f in session.Client.World.Level.AllActors<SFlashBang>())
            {
                flare |= !f.bDeleteMe;
            }

            foreach (SFlashLight l in session.Client.World.Level.AllActors<SFlashLight>())
            {
                light |= !l.bDeleteMe;
            }
        }

        session.Client.BeforeWorldTick -= Late;
        Assert.Equal(SDamageType.SDTFlash, server.LastDamages[0].DamageType);
        Assert.Equal(1, flashed);
        Assert.True(flare, "the SFlashBang flare did not reach the client");
        Assert.True(light, "the client did not light the flare");
    }
}
