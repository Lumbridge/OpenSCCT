using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Server;
using OpenScct.Net;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class SidearmNetworkTests
{
    private sealed class ShotSounds(bool includeReload = false) : ISimEventSink
    {
        public readonly List<SimEvent> Calls = [];
        public void OnSimEvents(long tick, ReadOnlySpan<SimEvent> events)
        {
            foreach (var e in events)
                if (e.Kind == SimEventKind.PlaySound &&
                    (e.What.ToString().EndsWith("_fire", StringComparison.Ordinal) ||
                     e.What.ToString().Contains("_1shot", StringComparison.OrdinalIgnoreCase) ||
                     (includeReload && e.What.ToString().EndsWith("_reload", StringComparison.Ordinal)))) Calls.Add(e);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyRevolverStaysSilentUntilReloadStartsAndPlaysOneSynchronizedReload(bool emptyTrigger)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("TestRange") is null, "TestRange content is required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "TestRange", Side = PlaySide.Merc, Rules = PlayRules.PlayLevel, Sidearms = true,
            HostMode = PlayHostMode.Inline, MatchSeed = 3, SimLink = SimLinkProfile.B,
        }, clock);
        void Tick(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
            }
        }
        for (int i = 0; i < 1200 && (session.LocalPawn is null || session.ServerController?.Pawn is null ||
            session.LocalController!.IsInState("WaitingForPawn")); i++) Tick();
        var serverPawn = Assert.IsAssignableFrom<SBasePawn>(session.ServerController!.Pawn);
        SidearmEquipment.Give(serverPawn, SidearmKind.Magnum44);
        Tick(90);
        session.LocalController!.SwapWeapon(); Tick(90);
        var pistol = session.LocalPawn!.SidearmWeapon!;
        var sounds = new ShotSounds(includeReload: true);
        var router = OpenScctGame.Play.PlayAudioRouter.Install(session.Client, sounds);
        bool IsReload(SimEvent e) => e.What == new Name(pistol.ReloadSound.Path);
        for (int round = 0; round < pistol.ClipMaxAmmo; round++)
        {
            session.Input.Set("bFire", true);
            session.Input.Commands.Add(new InputCommand("Fire")); Tick(2);
            session.Input.Set("bFire", false); Tick(90);
            Assert.Equal(pistol.ClipMaxAmmo - round - 1, pistol.ClipAmmo);
            Assert.DoesNotContain(sounds.Calls, IsReload);
        }
        Assert.Equal(0, serverPawn.SidearmWeapon!.ClipAmmo);
        Assert.Equal(0, pistol.ReloadUntil);
        Assert.Equal(pistol.ClipMaxAmmo, sounds.Calls.Count);
        if (emptyTrigger)
        {
            session.Input.Set("bFire", true);
            session.Input.Commands.Add(new InputCommand("Fire"));
        }
        else session.Input.Set("bReload", true);
        Tick();
        Assert.True(pistol.ReloadUntil > pistol.Level.TimeSeconds);
        Assert.Equal(pistol.AFirstReloSt, pistol.ViewAnim.Sequence(SWeaponDefense.ACTIONCHANNEL));
        Assert.True(pistol.ViewAnim.IsAnimating(SWeaponDefense.ACTIONCHANNEL));
        Assert.Equal(1f, pistol.ViewAnim.Alpha(SWeaponDefense.ACTIONCHANNEL));
        var reload = Assert.Single(sounds.Calls, IsReload);
        Assert.False(reload.Heard);
        Assert.True(reload.Owned);
        session.Input.Set("bReload", false);
        session.Input.Set("bFire", false);
        Tick((int)((pistol.Definition.ReloadSeconds + 1) * Sim.Hz));
        Assert.Single(sounds.Calls, IsReload);
        Assert.Equal(pistol.ClipMaxAmmo, pistol.ClipAmmo);
        Assert.Equal(pistol.ClipAmmo, serverPawn.SidearmWeapon.ClipAmmo);
        router.Uninstall();
    }

    [Theory]
    [InlineData(SidearmKind.Magnum44)]
    [InlineData(SidearmKind.Automatic)]
    public void LateJoinSeesTheActivePistolBothDummiesAndSeparateAmmunition(SidearmKind kind)
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("TestRange") is null, "TestRange content is required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "TestRange", Side = PlaySide.Merc, Rules = PlayRules.PlayLevel, Sidearms = true,
            HostMode = PlayHostMode.Inline, MatchSeed = 17, SimLink = SimLinkProfile.B,
        }, clock);
        ClientSession? observer = null;
        void Tick()
        {
            clock.NowMicroseconds += 1_000_000 / Sim.Hz;
            session.Step(clock.NowMicroseconds);
            session.Input.EndTick();
            observer?.Tick(clock.NowMicroseconds);
        }
        for (int i = 0; i < 1200 && (session.LocalPawn is not SPAWNDEFENSE ||
            session.LocalPawn.PrimaryWeapon is null || session.LocalController!.IsInState("WaitingForPawn")); i++) Tick();
        var pawn = Assert.IsAssignableFrom<SPAWNDEFENSE>(session.ServerController!.Pawn);
        SidearmEquipment.Give(pawn, kind);
        var pistol = pawn.SidearmWeapon!;
        pistol.ClipAmmo = 3; pistol.Ammo = 27; pistol.PublishCarriedAmmo();
        Assert.True(SidearmEquipment.Select(pawn, 1, force: true));
        for (int i = 0; i < 60; i++) Tick();
        if (kind == SidearmKind.Automatic) Assert.True(pistol.SetSidearmFireMode(SRateOfFireMode.ROF_Single));
        using var late = new ClientSession(session.SimLink!.Bind("late"), PlaySession.HostAddress,
            clock.NowMicroseconds, new ClientSettings { PlayerName = "Late", Gameplay = true, Maps = session.Catalog,
                InstallGameplayServices = false }, ownsTransport: true);
        observer = late;
        SBasePawn? seen = null;
        for (int i = 0; i < 1200; i++)
        {
            Tick();
            foreach (var channel in late.ActorChannels)
                if (channel.Actor is SPAWNDEFENSE candidate && candidate.PlayerReplicationInfo?.PlayerID == pawn.PlayerReplicationInfo!.PlayerID)
                    seen = candidate;
            if (seen?.SidearmWeapon?.Dummy is not null && seen.PrimaryWeapon is SWeaponDefense { Dummy: not null }) break;
        }
        Assert.NotNull(seen);
        Assert.True(seen.SidearmWeapon is not null,
            $"Late join missing pistol: server pawn deleted={pawn.bDeleteMe}, pistol deleted={pistol.bDeleteMe}, " +
            $"server carry={pawn.SidearmWeapon?.Kind}, seen pawn={seen.ActorId}, original pawn={pawn.ActorId}, late state={late.State}, " +
            $"primary={seen.PrimaryWeapon?.ActorId}, actor channels={late.ActorChannels.Count}");
        Assert.Equal(1, seen.ActiveWeaponSlot);
        Assert.Equal((int)kind, seen.SidearmWeapon!.Kind);
        Assert.Equal(SRateOfFireMode.ROF_Single, seen.SidearmWeapon.ROFMode);
        Assert.Same(seen.SidearmWeapon, seen.MyWeapon);
        Assert.Equal(3, seen.SidearmWeapon.ClipAmmo);
        Assert.Equal(27, seen.SidearmWeapon.Ammo);
        Assert.NotNull(seen.SidearmWeapon.Dummy);
        Assert.NotNull(((SWeaponDefense)seen.PrimaryWeapon!).Dummy);
    }

    [Fact]
    public void PistolsReplicateSwitchFireReloadAndCleanUpAcrossTwoWorlds()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("TestRange") is null, "TestRange content is required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "TestRange", Side = PlaySide.Merc, Rules = PlayRules.PlayLevel, Sidearms = true,
            HostMode = PlayHostMode.Inline, MatchSeed = 3, SimLink = SimLinkProfile.B,
        }, clock);
        void Tick(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                clock.NowMicroseconds += 1_000_000 / Sim.Hz;
                session.Step(clock.NowMicroseconds);
                session.Input.EndTick();
            }
        }
        for (int i = 0; i < 1200 && (session.LocalPawn is null || session.ServerController?.Pawn is null ||
            session.LocalController!.IsInState("WaitingForPawn")); i++) Tick();
        Assert.IsAssignableFrom<SPAWNDEFENSE>(session.LocalPawn);
        var sounds = new ShotSounds();
        var router = OpenScctGame.Play.PlayAudioRouter.Install(session.Client, sounds);
        var serverPawn = Assert.IsAssignableFrom<SBasePawn>(session.ServerController!.Pawn);
        foreach (var definition in Sidearms.All)
        {
            SidearmEquipment.Give(serverPawn, definition.Kind);
            Tick(90);
            var clientPawn = session.LocalPawn!;
            Assert.Equal((int)definition.Kind, clientPawn.SidearmWeapon!.Kind);
            Assert.NotNull(clientPawn.SidearmWeapon.Dummy);
            Assert.NotNull(((SWeaponDefense)clientPawn.PrimaryWeapon!).Dummy);
            Assert.Same(clientPawn.PrimaryWeapon, clientPawn.MyWeapon);
            session.LocalController!.SwapWeapon(); Tick(90);
            Assert.Equal(1, serverPawn.ActiveWeaponSlot);
            Assert.Same(clientPawn.SidearmWeapon, clientPawn.MyWeapon);
            Assert.Same(clientPawn.SidearmWeapon.Dummy, ((SPAWNDEFENSE)clientPawn).WeaponDummy);
            Assert.True(clientPawn.SidearmWeapon.bDrawWeapon);
            Assert.False(((SWeaponDefense)clientPawn.PrimaryWeapon!).bDrawWeapon);
            session.Input.Set("bFire", true);
            session.Input.Commands.Add(new InputCommand("Fire"));
            sounds.Calls.Clear();
            Tick();
            Assert.Single(sounds.Calls);
            Assert.False(sounds.Calls[0].Heard);
            Assert.True(sounds.Calls[0].Owned);
            if (definition.Automatic) Tick(35);
            session.Input.Set("bFire", false);
            Tick(90);
            int fired = definition.Magazine - serverPawn.SidearmWeapon!.ClipAmmo;
            Assert.Equal(fired, sounds.Calls.Count);
            Assert.DoesNotContain(sounds.Calls, e => e.Heard);
            Assert.InRange(fired, definition.Automatic ? 5 : 1, definition.Automatic ? 7 : 1);
            Assert.Equal(serverPawn.SidearmWeapon.ClipAmmo, clientPawn.SidearmWeapon.ClipAmmo);
            if (definition.Automatic)
            {
                session.Input.Commands.Add(new InputCommand("SwitchROF")); Tick(30);
                Assert.Equal(SRateOfFireMode.ROF_Single, clientPawn.SidearmWeapon.ROFMode);
                Assert.Equal(SRateOfFireMode.ROF_Single, serverPawn.SidearmWeapon.ROFMode);
                int beforeSingle = serverPawn.SidearmWeapon.ClipAmmo;
                session.Input.Set("bFire", true);
                session.Input.Commands.Add(new InputCommand("Fire")); Tick(36);
                session.Input.Set("bFire", false); Tick(30);
                Assert.Equal(beforeSingle - 1, serverPawn.SidearmWeapon.ClipAmmo);
                Assert.Equal(serverPawn.SidearmWeapon.ClipAmmo, clientPawn.SidearmWeapon.ClipAmmo);
                session.Input.Commands.Add(new InputCommand("SwitchROF")); Tick(30);
                Assert.Equal(SRateOfFireMode.ROF_Auto, serverPawn.SidearmWeapon.ROFMode);
                Assert.Equal(SRateOfFireMode.ROF_Auto, clientPawn.SidearmWeapon.ROFMode);
            }
            Assert.True(clientPawn.SidearmWeapon.Reload());
            Tick((int)((definition.ReloadSeconds + 1) * Sim.Hz));
            Assert.Equal(definition.Magazine, serverPawn.SidearmWeapon.ClipAmmo);
            Assert.Equal(serverPawn.SidearmWeapon.ClipAmmo, clientPawn.SidearmWeapon.ClipAmmo);
            session.LocalController.SwapWeapon(); Tick(90);
            Assert.Equal(0, serverPawn.ActiveWeaponSlot);
            Assert.True(((SWeaponDefense)clientPawn.PrimaryWeapon!).bDrawWeapon);
            Assert.False(clientPawn.SidearmWeapon.bDrawWeapon);
            if (definition.Kind == SidearmKind.Suppressed)
            {
                var primary = (SWeaponDefense)serverPawn.PrimaryWeapon!;
                sounds.Calls.Clear();
                session.Input.Set("bFire", true);
                session.Input.Commands.Add(new InputCommand("Fire")); Tick();
                Assert.Single(sounds.Calls);
                Assert.False(sounds.Calls[0].Heard);
                Assert.True(sounds.Calls[0].Owned);
                session.Input.Set("bFire", false); Tick(90);
                Assert.Single(sounds.Calls);
                primary.ClipAmmo -= 3; primary.PublishCarriedAmmo(); Tick(90);
                Assert.True(((SWeaponDefense)clientPawn.PrimaryWeapon!).Reload());
                Tick(300);
                Assert.Equal(primary.ClipMaxAmmo, primary.ClipAmmo);
                Assert.Equal(primary.ClipAmmo, ((SWeaponDefense)clientPawn.PrimaryWeapon).ClipAmmo);
                Assert.Equal(definition.Magazine, serverPawn.SidearmWeapon.ClipAmmo);
            }
            serverPawn.SidearmWeapon.Dummy?.Destroy();
            serverPawn.SidearmWeapon.Destroy(); serverPawn.SidearmWeapon = null;
            Tick(45);
        }
        router.Uninstall();
    }
}
