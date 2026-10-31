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
    [Fact]
    public void LateJoinSeesTheActivePistolBothDummiesAndSeparateAmmunition()
    {
        Assert.SkipWhen(MapCatalog.CreateDefault().FindFolder("TestRange") is null, "TestRange content is required.");
        var clock = new StepClock();
        using var session = new PlaySession(new PlaySettings
        {
            Map = "TestRange", Side = PlaySide.Merc, Rules = PlayRules.PlayLevel,
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
        SidearmEquipment.Give(pawn, SidearmKind.Magnum44);
        var pistol = pawn.SidearmWeapon!;
        pistol.ClipAmmo = 3; pistol.Ammo = 27; pistol.PublishCarriedAmmo();
        Assert.True(SidearmEquipment.Select(pawn, 1, force: true));
        for (int i = 0; i < 60; i++) Tick();
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
        Assert.Equal((int)SidearmKind.Magnum44, seen.SidearmWeapon!.Kind);
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
            Map = "TestRange", Side = PlaySide.Merc, Rules = PlayRules.PlayLevel,
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
            session.Input.Set("bFire", true);
            session.Input.Commands.Add(new InputCommand("Fire"));
            Tick(definition.Automatic ? 36 : 1);
            session.Input.Set("bFire", false);
            Tick(90);
            int fired = definition.Magazine - serverPawn.SidearmWeapon!.ClipAmmo;
            Assert.InRange(fired, definition.Automatic ? 5 : 1, definition.Automatic ? 7 : 1);
            Assert.Equal(serverPawn.SidearmWeapon.ClipAmmo, clientPawn.SidearmWeapon.ClipAmmo);
            Assert.True(clientPawn.SidearmWeapon.Reload());
            Tick((int)((definition.ReloadSeconds + 1) * Sim.Hz));
            Assert.Equal(definition.Magazine, serverPawn.SidearmWeapon.ClipAmmo);
            Assert.Equal(serverPawn.SidearmWeapon.ClipAmmo, clientPawn.SidearmWeapon.ClipAmmo);
            session.LocalController.SwapWeapon(); Tick(90);
            Assert.Equal(0, serverPawn.ActiveWeaponSlot);
            if (definition.Kind == SidearmKind.Suppressed)
            {
                var primary = (SWeaponDefense)serverPawn.PrimaryWeapon!;
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
    }
}
