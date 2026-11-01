using System.Linq;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
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
