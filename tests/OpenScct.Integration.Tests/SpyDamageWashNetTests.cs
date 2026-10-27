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
/// The spy's damage overlay over the wire (spec/hud/screen-effects.md rule 7): a bullet hit on the host reaches the
/// connected spy's machine as ClientDamage, whose ClientDamageHudEffect gives his HUD the red wash. The call lands
/// between the client's ticks, so it is one of the world's late events; the play view hands those to the HUD before
/// the step (PlayRoot, HudEventTap.TakeLate). Before that the step dropped them and the spy never saw the wash.
/// </summary>
public sealed class SpyDamageWashNetTests
{
    [Fact]
    public void A_hit_on_the_host_gives_the_connected_spy_his_damage_hud_effect()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is not in this checkout.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = MapCatalog.TestBox,
            Side = PlaySide.Spy,
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

        Assert.Equal(PawnType.SPAWNOFFENSE, server.SPawnType);
        Pawn spy = server.Pawn!;
        int healthBefore = spy.Health;
        server.PawnTakeDamage(10, null, spy.Location, new FVector(-1f, 0f, 0f), SDamageType.SDTBase, 2, 70);

        int inTicks = 0;
        int late = 0;
        int Count(ReadOnlySpan<SimEvent> events)
        {
            int n = 0;
            foreach (SimEvent e in events)
            {
                if (e.Kind == SimEventKind.Notify && e.What == CombatFx.ClientDamageHudEffect && e.ActorId == client.ActorId
                    && e.Slot == (int)SDamageType.SDTBase)
                {
                    n++;
                }
            }

            return n;
        }

        void Late(ClientSession c) => late += Count(c.World.Events.Late);
        session.Client.BeforeWorldTick += Late;
        for (int i = 0; i < Sim.Hz && inTicks + late == 0; i++)
        {
            Tick();
            inTicks += Count(session.Client.World.Events.Current);
        }

        session.Client.BeforeWorldTick -= Late;
        Assert.True(spy.Health < healthBefore, "the hit did no damage on the host");
        // One effect, and it is a late event: a HUD that read only the ticks' events (the old play view) missed it.
        Assert.Equal(1, inTicks + late);
        Assert.Equal(1, late);
        Assert.Equal(PawnType.SPAWNOFFENSE, client.SPawnType);
        Assert.IsAssignableFrom<SPawnAttaque>(client.Pawn);
    }
}
