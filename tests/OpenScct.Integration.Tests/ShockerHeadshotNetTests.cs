using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class ShockerHeadshotNetTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ConnectedMercReceivesShockEffectsAndOwnerSounds(int pill)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder(MapCatalog.TestBox) is null,
            "The TestBox fixture is not in this checkout.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = MapCatalog.TestBox, Side = PlaySide.Merc, Rules = PlayRules.PlayLevel,
            HostMode = PlayHostMode.Inline, MatchSeed = 7,
        }, clock);
        void Tick()
        {
            clock.NowMicroseconds += 1_000_000 / Sim.Hz;
            session.Step(clock.NowMicroseconds);
            session.Input.EndTick();
        }

        for (int i = 0; i < 1200 && (session.LocalPawn is null || session.ServerController?.Pawn is null ||
            session.LocalController!.IsInState("WaitingForPawn")); i++) Tick();
        SPlayerController client = session.LocalController ?? throw new InvalidOperationException("No local controller");
        SPlayerController server = session.ServerController!;
        for (int i = 0; i < 120; i++) Tick();
        var hud = Assert.IsType<SHud>(client.myHUD);
        var merc = Assert.IsAssignableFrom<SPAWNDEFENSE>(server.Pawn);
        var localMerc = Assert.IsAssignableFrom<SPAWNDEFENSE>(client.Pawn);
        int health = merc.Health;
        server.TimeShock = -100f;

        var events = new List<SimEvent>();
        void Capture(ReadOnlySpan<SimEvent> batch)
        {
            foreach (SimEvent e in batch)
                if (e.ActorId == client.ActorId || e.ActorId == localMerc.ActorId) events.Add(e);
        }
        // Owner RPCs execute between ticks; PlayRoot also drains these late events for audio and the HUD.
        void Late(ClientSession c) => Capture(c.World.Events.Late);
        session.Client.BeforeWorldTick += Late;
        server.PawnTakeDamage(0, null, merc.Location, default, SDamageType.SDTStickyShocker, pill);
        for (int i = 0; i < Sim.Hz; i++)
        {
            Tick();
            Capture(session.Client.World.Events.Current);
        }
        session.Client.BeforeWorldTick -= Late;

        Assert.Equal(health, merc.Health);
        Assert.True(localMerc.bStickyShocking);
        Assert.Equal(pill == 1, localMerc.bStickyShockedInTheHead);
        Assert.Equal(pill == 1, hud.bShutDown);
        Assert.True(hud.bPerturbation);
        Assert.Single(events, e => e.Kind == SimEventKind.Notify && e.What == CombatFx.SetHudPerturbationLength);
        Assert.Equal(pill == 1 ? 1 : 0,
            events.Count(e => e.Kind == SimEventKind.Notify && e.What == CombatFx.HudShutDown));
        AssertSound("Armes_impacts.Armes.StikyShok_Paf1", 1);
        AssertSound("Sound_Persos.onomatopees.RA_onoPaf", 1);
        AssertSound("Armes_impacts.Gadgets.Hud_shutDown", pill == 1 ? 1 : 0);

        void AssertSound(string path, int count)
        {
            SimEvent[] sounds = events.Where(e => e.Kind == SimEventKind.PlaySound && e.What == new Name(path)).ToArray();
            Assert.Equal(count, sounds.Length);
            Assert.All(sounds, e => Assert.True(e.Flat, "Owner feedback must play without positional attenuation"));
        }
    }
}
