using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Integration.Tests.Online;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server.Online;
using OpenScctGame.Play.Session;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>Aquarius Tech Room doors: presence triggers, delayed releases and replicated movement.</summary>
public sealed class MoverNetworkTests
{
    [Fact]
    public void SoloCreatedSessionClosesTechRoomDoors()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("AquaE") is null, "Map is not converted.");
        var clock = new VirtualClock();
        var network = new LoopbackNetwork();
        var settings = OnlineRig.Settings("AquaE") with { Dedicated = false, HostKey = "k", AutoLaunch = false };
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, 0), clock);
        int n = 0;
        var target = JoinTarget.Custom("Test", () => network.Bind("host" + n++), "server");
        var host = rig.Add(new OnlineClient(target, OnlineRig.ClientSettings("Host", 1) with { HostKey = "k" }, 0));
        Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Lobby && host.Lobby.Local is { Map: MapState.Ok }, 15));
        host.Launch();
        Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Match && host.Session?.LocalPlayer?.Pawn is not null, 30));
        var pawn = rig.Server.Players.Single().Slot!.Controller!.Pawn!;
        foreach (string tag in new[] { "Porte04", "Porte08" })
        {
            var door = rig.Server.Host.Map.MapActors.OfType<Mover>().Single(m => m.Tag == tag);
            var copy = host.Session!.Map!.MapActors.OfType<Mover>().Single(m => m.Tag == tag);
            var trigger = rig.Server.Host.Map.MapActors.OfType<SPresenceTrigger>().First(m => m.Event == "SM" + tag);
            var closed = door.Location;
            door.bAlwaysRelevant = true;
            trigger.eventTouch(pawn);
            rig.RunUntil(() => false, 3.5);
            Assert.True((door.Location - closed).Size() > 100, tag + " must open");
            Assert.True((copy.Location - door.Location).Size() < 3, tag + " client open");
            rig.RunUntil(() => false, 5);
            Assert.True(door.KeyNum == 0 && !door.bInterpolating, tag + " server close");
            Assert.True((copy.Location - closed).Size() < 3, tag + " client close");
        }
    }

    [Theory]
    [InlineData(PlaySide.Merc, "Porte04")]
    [InlineData(PlaySide.Spy, "Porte04")]
    [InlineData(PlaySide.Merc, "Porte08")]
    [InlineData(PlaySide.Spy, "Porte08")]
    [InlineData(PlaySide.Merc, "Porte04", true)]
    [InlineData(PlaySide.Merc, "Porte08", true)]
    public void TechRoomDoorClosesAfterPlayerPasses(PlaySide side, string tag, bool serverStartedEarlier = false)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("AquaE") is null, "Map is not converted.");
        using var run = new PlacedPlayer("AquaE", side);
        var door = run.Session.Host.Map.MapActors.OfType<Mover>().Single(m => m.Tag == tag);
        var copy = run.Session.Client.Map!.MapActors.OfType<Mover>().Single(m => m.Tag == tag);
        var relay = run.Session.Host.Map.MapActors.OfType<SMagicEvent>().Single(m => m.Tag == "SM" + tag);
        var closed = door.Location;
        // A real client can finish loading several seconds after the server. The fast in-process
        // fixture otherwise hides the input/world clock offset that made the release fire immediately.
        if (serverStartedEarlier)
        {
            for (int i = 0; i < 300; i++) run.Session.Host.World.Step();
        }
        run.Place(tag == "Porte04" ? new FVector(4900, 704, 220) : new FVector(7152, 1850, 220), tag == "Porte04" ? 0 : 49152);
        bool opened = false;
        run.Hold(["BT_Up"], 6, () => opened |= (door.Location - closed).Size() > 100);
        Assert.True(opened, $"Must open: pawn={run.Server.Location} door={door.Location} key={door.KeyNum} count={door.numTriggerEvents} queue={relay.EventQueue.Count}");
        run.Ticks(600);
        Assert.True(door.KeyNum == 0 && !door.bInterpolating,
            $"key={door.KeyNum} count={door.numTriggerEvents} queue={relay.EventQueue.Count} pawn={run.Server.Location} wait={door.StateFrame.Wait} pc={door.StateFrame.Pc}");
        Assert.True((copy.Location - closed).Size() < 3, $"client={copy.Location} server={door.Location} clientKey={copy.KeyNum}");
    }
}

