using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class WeaponLifetimeNetworkTests
{
    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => BotInput.Idle;
    }

    [Fact]
    public void StandardRifleOwnerAndAuthorityAgreeAfterFireAndReload()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 615, SimLinkProfile.Get("200"));
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
        Tick(900);
        var bot = match.Bots[2];
        var owner = Assert.IsAssignableFrom<SBasePawn>(bot.Controller!.Pawn);
        var source = Assert.IsAssignableFrom<SBasePawn>(match.ServerController(bot)!.Pawn);
        var gun = Assert.IsType<SWeaponDefense>(owner.MyWeapon);
        var authority = Assert.IsType<SWeaponDefense>(source.MyWeapon);
        int ammo = gun.Ammo, clip = gun.ClipAmmo;
        gun.FireWeapon();
        Tick(120);
        Assert.Equal(clip - 1, gun.ClipAmmo);
        Assert.Equal(ammo - 1, gun.Ammo);
        Assert.Equal(gun.ClipAmmo, authority.ClipAmmo);
        Assert.Equal(gun.Ammo, authority.Ammo);
        Assert.True(gun.eventReload());
        Tick(360);
        Assert.Equal(clip, gun.ClipAmmo);
        Assert.Equal(ammo - 1, gun.Ammo);
        Assert.Equal(gun.ClipAmmo, authority.ClipAmmo);
        Assert.Equal(gun.Ammo, authority.Ammo);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ShotInFlightCannotFireAfterItsShooterDies(int shooterIndex)
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 614, SimLinkProfile.Get("200"));
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
        Tick(900);
        var bot = match.Bots[shooterIndex];
        var owner = Assert.IsAssignableFrom<SPlayerController>(bot.Controller);
        var source = match.ServerController(bot)!;
        var body = Assert.IsAssignableFrom<SBasePawn>(source.Pawn);
        var localBody = Assert.IsAssignableFrom<SBasePawn>(owner.Pawn);
        var gun = Assert.IsAssignableFrom<SWeapon>(localBody.MyWeapon);
        var sourceGun = Assert.IsAssignableFrom<SWeapon>(body.MyWeapon);
        var bridge = bot.Session.World.Level.GetService<NetCombatBridge>()!;
        void Shot(int sequence) => bridge.ServerCall(owner, shooterIndex == 0
            ? new CombatCall(new Name("ServerFireWeaponWithLocation")) { Target = gun, Int1 = sequence, Vector1 = localBody.Location }
            : new CombatCall(new Name("ServerFireWeapon")) { Target = gun, Int2 = sequence });
        Shot(1);
        Tick(60);
        Assert.Equal(1, sourceGun.ShotCounter);
        Shot(2);
        source.PawnTakeDamage(1000, null, body.Location, default, SDamageType.SDTBase, 2, 0);
        Assert.True(body.Health <= 0);
        Tick(60);
        Assert.Equal(1, sourceGun.ShotCounter);
        Assert.Equal(EClientState.Joined, bot.Session.State);
    }
}
