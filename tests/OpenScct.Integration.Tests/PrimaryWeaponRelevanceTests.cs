using System.Linq;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class PrimaryWeaponRelevanceTests
{
    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => BotInput.Idle;
    }

    [Theory]
    [InlineData(WeaponAttachments.M4, 30, 80)]
    [InlineData("Svm.Galil", 30, 105)]
    [InlineData("SBase.SWeaponDefense_Uzi", 30, 300)]
    [InlineData("Svm.CompactSMG", 30, 100)]
    [InlineData("Svm.PumpShotgun", 6, 44)]
    [InlineData("Svm.BoltRifle", 5, 40)]
    [InlineData("Svm.DesignatedMarksman", 30, 60)]
    public void OwnerObserverAndLateJoinerReceiveBothWeaponMasksAndMagazineCapacity(string gun, int capacity, int reserve)
    {
        int primaryMask = capacity == 30 ? WeaponAttachments.Acog | WeaponAttachments.ExtendedMagazine : WeaponAttachments.Acog;
        const int sidearmMask = WeaponAttachments.RedDot;
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 1853, SimLinkProfile.Get("100"));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            Bots = [new BotSpec("Spy", BotTeam.Spy, () => new IdleBrain()), new BotSpec("Merc", BotTeam.Merc, () => new IdleBrain())],
        }, network.Bind, "server", 0);
        ClientSession? later = null;
        long frame = 0;
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                match.Step(clock.NowMicroseconds);
                later?.Tick(clock.NowMicroseconds);
            }
        }

        Tick(600);
        var bot = match.Bots[1];
        var pc = match.ServerController(bot)!;
        var pawn = Assert.IsAssignableFrom<SPAWNDEFENSE>(pc.Pawn);
        var owner = Assert.IsAssignableFrom<SPAWNDEFENSE>(bot.Controller!.Pawn);
        SPAWNDEFENSE AsSeenBy(ClientSession client) => Assert.IsAssignableFrom<SPAWNDEFENSE>(
            client.ActorChannels.Single(c => c.NetGuid == pawn.ActorId).Actor);
        var observer = AsSeenBy(match.Bots[0].Session);
        pc.GameReplicationInfo!.RiflesEnabled = true;
        pc.GameReplicationInfo.SidearmsEnabled = true;
        pawn.PlayerReplicationInfo!.SvmRank = 99;
        if (gun == "SBase.SWeaponDefense_Uzi")
            Assert.IsAssignableFrom<STeamInfo>(pawn.PlayerReplicationInfo.Team).ClassWeapon = [new ClassRef(gun)];
        string pistol = Sidearms.Get(SidearmKind.Magnum44).CarryId;
        for (int i = 0; i < 50; i++) WeaponCareer.RecordKill(pc, gun);
        for (int i = 0; i < 10; i++) WeaponCareer.RecordKill(pc, pistol);
        PawnInventory.DeleteAllInventory(pawn);
        Loadouts.OpenGrant(pawn, EGameMode.GM_Multi);
        Loadouts.ServerSpawnWeapon(owner, new ClassRef(gun), primaryMask);
        Tick(90);
        Assert.Equal(primaryMask, observer.PrimaryAttachmentMask);
        Assert.Null(observer.PrimaryWeapon);
        Assert.Equal(capacity, Assert.IsAssignableFrom<SWeaponDefense>(owner.PrimaryWeapon).ClipAmmo);

        Loadouts.ServerSpawnInventory(owner, new ClassRef(pistol), sidearmMask);
        Tick(120);
        Assert.True(SidearmEquipment.Select(pawn, 1, force: true));
        using var arriving = new ClientSession(network.Bind("late"), "server", clock.NowMicroseconds,
            new ClientSettings { PlayerName = "Late", Gameplay = true, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes },
            ownsTransport: true);
        later = arriving;
        Tick(360);
        Assert.Equal(EClientState.Joined, arriving.State);
        var views = new[] { pawn, owner, observer, AsSeenBy(arriving) };
        foreach (var view in views)
        {
            Assert.Equal(primaryMask, view.PrimaryAttachmentMask);
            var rifle = Assert.IsAssignableFrom<SWeaponDefense>(view.PrimaryWeapon);
            Assert.Equal(gun, WeaponAttachments.Key(rifle));
            Assert.Equal(primaryMask, rifle.AttachmentMask);
            Assert.Equal(capacity, rifle.ClipMaxAmmo);
            Assert.Equal(capacity + reserve, rifle.MaxAmmo);
            Assert.Equal(sidearmMask, Assert.IsType<MercSidearm>(view.SidearmWeapon).AttachmentMask);
            Assert.Equal(1, view.ActiveWeaponSlot);
            Assert.Same(view.SidearmWeapon, view.MyWeapon);
        }
        // The fitted sidearm and replacement pump both use the same validated owner-to-server sight action.
        // Kill achievements classify optics from this authority state when a fatal shot lands.
        var ownerSidearm = Assert.IsType<MercSidearm>(owner.MyWeapon);
        ownerSidearm.SetAiming(bot.Controller!, true);
        Tick(30);
        Assert.True(Assert.IsType<MercSidearm>(pawn.MyWeapon).AimRequested);
        ownerSidearm.SetAiming(bot.Controller!, false);
        Tick(30);
        Assert.False(Assert.IsType<MercSidearm>(pawn.MyWeapon).AimRequested);
        Assert.True(SidearmEquipment.Select(pawn, 0, force: true));
        Tick(100);
        foreach (var view in views)
        {
            Assert.Same(view.PrimaryWeapon, view.MyWeapon);
            Assert.Equal(primaryMask, Assert.IsAssignableFrom<SWeaponDefense>(view.MyWeapon).AttachmentMask);
        }
        var drawn = Assert.IsAssignableFrom<SWeaponDefense>(owner.MyWeapon);
        drawn.SetAiming(bot.Controller!, true);
        Tick(30);
        Assert.True(Assert.IsAssignableFrom<SWeaponDefense>(pawn.MyWeapon).AimRequested);
        drawn.FireWeapon();
        Tick(120);
        foreach (var view in views)
        {
            var copy = Assert.IsAssignableFrom<SWeaponDefense>(view.MyWeapon);
            Assert.Equal(capacity - 1, copy.ClipAmmo);
            Assert.Equal(1, copy.CarriedShot);
        }
    }

    [Fact]
    public void PistolAimIsVisibleAcrossTheNetworkAndClearsOnReloadHolsterAndRespawn()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 1298, SimLinkProfile.Get("100"));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            GameOptions = "?GameMode=5?TimeLimit=0?LivesATT=4?LivesDEF=4?TimesRespawnsATT=1",
            Bots = BotMatchSettings.TwoVersusTwo(_ => new IdleBrain(), _ => new IdleBrain()),
        }, network.Bind, "server", 0);
        long frame = 0;
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                match.Step(clock.NowMicroseconds);
            }
        }
        Tick(900);
        var merc = match.Bots[2];
        var client = merc.Controller!;
        var server = match.ServerController(merc)!;
        var pawn = Assert.IsAssignableFrom<SPAWNDEFENSE>(server.Pawn);
        SidearmEquipment.Give(pawn, SidearmKind.Unsuppressed);
        Tick(90);
        client.SwapWeapon();
        Tick(90);
        var pistol = Assert.IsType<MercSidearm>(client.SBPawn!.MyWeapon);
        var authority = Assert.IsType<MercSidearm>(pawn.MyWeapon);
        var observer = Assert.IsAssignableFrom<SPAWNDEFENSE>(match.Bots[0].Session.ActorChannels
            .Single(c => c.NetGuid == pawn.ActorId).Actor);
        var remote = Assert.IsType<MercSidearm>(observer.MyWeapon);

        void AssertAim(bool aimed)
        {
            Assert.Equal(aimed, pistol.AimRequested);
            Assert.Equal(aimed, authority.AimRequested);
            Assert.Equal(aimed, remote.AimRequested);
            float scale = aimed ? OpenScct.Game.Svm.MercRifle.AimSpeedScale : 1f;
            Assert.Equal(scale, new SBasePawnMoves(client).GroundSpeedMultiplier);
            Assert.Equal(scale, new SBasePawnMoves(server).GroundSpeedMultiplier);
        }

        pistol.SetAiming(client, true);
        Tick(60);
        AssertAim(true);
        authority.ClipAmmo--;
        authority.Ammo--;
        authority.PublishCarriedAmmo();
        Tick(45);
        Assert.True(pistol.eventReload());
        Tick(20);
        Assert.True(authority.ReloadUntil > authority.Level.TimeSeconds);
        AssertAim(false);
        pistol.SetAiming(client, true);
        Tick(15);
        AssertAim(false);
        Tick((int)(pistol.Definition.ReloadSeconds * Sim.Hz) + 30);
        Assert.Equal(pistol.ClipMaxAmmo, authority.ClipAmmo);

        pistol.SetAiming(client, true);
        Tick(60);
        AssertAim(true);
        client.SwapWeapon();
        Tick(90);
        Assert.Same(pawn.PrimaryWeapon, pawn.MyWeapon);
        AssertAim(false);
        client.SwapWeapon();
        Tick(90);
        AssertAim(false);

        pistol.SetAiming(client, true);
        Tick(60);
        AssertAim(true);
        server.PawnTakeDamage(1000, null, pawn.Location, default, SDamageType.SDTBase);
        Tick(660);
        var replacement = Assert.IsAssignableFrom<SPAWNDEFENSE>(server.Pawn);
        Assert.NotSame(pawn, replacement);
        SidearmEquipment.Give(replacement, SidearmKind.Unsuppressed);
        Tick(90);
        client.SwapWeapon();
        Tick(90);
        Assert.False(Assert.IsType<MercSidearm>(client.SBPawn!.MyWeapon).AimRequested);
        Assert.False(Assert.IsType<MercSidearm>(replacement.MyWeapon).AimRequested);
        Assert.Equal(1f, new SBasePawnMoves(server).GroundSpeedMultiplier);
    }

    [Fact]
    public void SingleRifleObserversDoNotRetryAnInvisibleInventoryReferenceAndCanReceiveALaterSidearm()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 8817, SimLinkProfile.Get("100"));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
            Bots = BotMatchSettings.TwoVersusTwo(_ => new IdleBrain(), _ => new IdleBrain()),
        }, network.Bind, "server", 0);
        long frame = 0;
        void Tick(int count)
        {
            for (int i = 0; i < count; i++)
            {
                clock.AdvanceTo(VirtualClock.TickToMicroseconds(++frame, Sim.Hz));
                match.Step(clock.NowMicroseconds);
            }
        }

        Tick(600);
        var pawn = Assert.IsAssignableFrom<SPAWNDEFENSE>(match.ServerController(match.Bots[2])!.Pawn);
        var owner = Assert.IsAssignableFrom<SPAWNDEFENSE>(match.Bots[2].Controller!.Pawn);
        var observer = Assert.IsAssignableFrom<SPAWNDEFENSE>(match.Bots[0].Session.ActorChannels
            .Single(c => c.NetGuid == pawn.ActorId).Actor);
        var connection = match.Server.Players.Single(p => p.Name == match.Bots[0].Name).Connection;
        var channel = connection.FindActorChannel(pawn)!;
        Assert.Null(pawn.SidearmWeapon);
        Assert.Same(owner.MyWeapon, owner.PrimaryWeapon);
        Assert.NotNull(owner.PrimaryWeapon);
        Assert.Null(observer.PrimaryWeapon);
        Assert.NotNull(observer.WeaponDummy);
        Assert.False(channel.HasDirtyProperties, "The observer must not keep retrying the hidden first-person rifle.");
        int before = channel.PropertyBunchesSent;
        Tick(120);
        Assert.InRange(channel.PropertyBunchesSent - before, 0, 2);

        SidearmEquipment.Give(pawn, SidearmKind.Magnum44);
        Tick(180);
        Assert.NotNull(observer.PrimaryWeapon);
        Assert.NotNull(observer.SidearmWeapon?.Dummy);
        Assert.NotNull(Assert.IsAssignableFrom<SWeaponDefense>(observer.PrimaryWeapon).Dummy);
        var carriedPrimary = observer.PrimaryWeapon;
        pawn.SidearmWeapon!.Dummy?.Destroy();
        pawn.SidearmWeapon.Destroy();
        pawn.SidearmWeapon = null;
        Tick(120);
        Assert.Same(carriedPrimary, observer.PrimaryWeapon);
        Assert.False(carriedPrimary.bDeleteMe);
        Assert.Same(owner.MyWeapon, owner.PrimaryWeapon);
    }
}
