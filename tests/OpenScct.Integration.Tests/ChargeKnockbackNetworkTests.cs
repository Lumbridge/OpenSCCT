using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class ChargeKnockbackNetworkTests
{
    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => BotInput.Idle;
    }

    [Theory]
    [InlineData(NetProfile.Enhanced)]
    [InlineData(NetProfile.Stock)]
    public void SpyLiesDownAtTheSameSpotOnClientAndServerAfterAMercCharge(NetProfile profile)
    {
        MapCatalog maps = WalkerRig.Maps;
        Assert.SkipWhen(maps.FindCharacterContent() is null, "Charge knockback requires the converted root-motion clip.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 973, SimLinkProfile.Ping200);
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = MapCatalog.TestBox,
            Maps = maps,
            Classes = WalkerRig.Classes,
            Bots = BotMatchSettings.TwoVersusTwo(_ => new IdleBrain(), _ => new IdleBrain()),
            Profile = profile,
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
        BotClient spy = match.Bots[0], merc = match.Bots[2];
        var owner = Assert.IsType<SPlayerController>(spy.Controller);
        var authority = Assert.IsType<SPlayerController>(match.ServerController(spy));
        var attacker = Assert.IsType<SPlayerController>(match.ServerController(merc));
        var local = Assert.IsAssignableFrom<SBasePawn>(owner.Pawn);
        var remote = Assert.IsAssignableFrom<SBasePawn>(authority.Pawn);
        var mercLocal = Assert.IsAssignableFrom<SBasePawn>(merc.Controller!.Pawn);
        var mercRemote = Assert.IsAssignableFrom<SBasePawn>(attacker.Pawn);
        void Place(SBasePawn pawn, float x)
        {
            Assert.True(pawn.SetLocation(new FVector(x, 300f, pawn.Location.Z)));
            pawn.Velocity = pawn.Acceleration = default;
        }

        // Facing west toward the Merc puts the knockback yaw beyond the old scaled vector's range.
        Place(local, 600f);
        Place(remote, 600f);
        Place(mercLocal, 460f);
        Place(mercRemote, 460f);
        Tick(120);
        FVector start = remote.Location;
        Assert.InRange(FVector.Dist(local.Location, start), 0f, 2f);
        attacker.DamagePawn_C2S(remote, 35, mercRemote, start, start - mercRemote.Location,
            SDamageType.SDamage_Charged, 0, 100);

        for (int i = 0; i < 4 * Sim.Hz && !(local.bLyingOnGround && remote.bLyingOnGround); i++) Tick(1);
        Assert.True(local.bLyingOnGround && remote.bLyingOnGround,
            $"Owner {owner.GetStateName()} at {local.Location}, authority {authority.GetStateName()} at {remote.Location}.");
        Assert.True(local.Location.X - start.X > 200f, $"The charge must displace the Spy. Start {start}, owner {local.Location}.");
        Assert.True(FVector.Dist(local.Location, remote.Location) < 3f,
            $"Owner landed at {local.Location} (yaw {local.SpecialYaw}), authority at {remote.Location} (yaw {remote.SpecialYaw}).");

        for (int i = 0; i < 10 * Sim.Hz && !(owner.IsInState("s_PlayerWalking") && authority.IsInState("s_PlayerWalking")); i++)
            Tick(1);
        Assert.True(owner.IsInState("s_PlayerWalking"), $"Owner is still in {owner.GetStateName()}.");
        Assert.True(authority.IsInState("s_PlayerWalking"), $"Authority is still in {authority.GetStateName()}.");
        Assert.True(FVector.Dist(local.Location, remote.Location) < 3f,
            $"Owner woke up at {local.Location}, authority at {remote.Location}.");
        Assert.Equal(EClientState.Joined, spy.Session.State);
    }
}
