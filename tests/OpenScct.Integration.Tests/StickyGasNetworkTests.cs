using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class StickyGasNetworkTests
{
    private sealed class IdleBrain : BotBrain
    {
        public override BotInput Think(BotContext context) => BotInput.Idle;
    }

    [Theory]
    [InlineData("TestBox", 0)]
    [InlineData("TestBox", 16384)]
    [InlineData("AquaE", 0)]
    [InlineData("AquaE", 16384)]
    public void SpyCameraGasReachesMercWithItsPositionAndDirection(string map, int yaw)
    {
        Assert.SkipWhen(map != "TestBox" && WalkerRig.Maps.FindFolder(map) is null, "Map is not converted.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 9157, SimLinkProfile.Get("200"));
        using var match = new BotMatch(new BotMatchSettings
        {
            Map = map, Maps = WalkerRig.Maps, Classes = WalkerRig.Classes,
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
        var spy = match.ServerController(match.Bots[0])!;
        var merc = match.ServerController(match.Bots[2])!.Pawn!;
        var source = spy.XLevel.SpawnActor(new SStickyCam(), merc.Location + new FVector(0, 0, 140), new FRotator(0, yaw, 0), spy.Pawn)!;
        source.SpawnGaz();
        Tick(30);
        SStickyCamEmitter? gas = null;
        foreach (var emitter in match.Bots[2].Session.World.Level.AllActors<SStickyCamEmitter>())
        {
            Assert.Null(gas);
            gas = emitter;
        }
        Assert.NotNull(gas);
        Assert.Equal(source.Location, gas.Location);
        Assert.Equal(yaw, gas.Rotation.Yaw);
        Assert.Equal(gas.Location + gas.Rotation.Vector() * 192, gas.RefLocation);
        Assert.False(gas.bHidden);
        Assert.False(gas.bNoDelete);
        Tick(420);
        Assert.True(gas.bDeleteMe);
    }
}
