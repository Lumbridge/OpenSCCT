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
    [Theory]
    [InlineData("SteeE", "PorteMerc", "C")]
    [InlineData("AquaE", "Porte04", "A")]
    [InlineData("AquaE", "Porte08", "300")]
    public void AuthoredPresenceDoorStaysOpenForAStationaryMercAndReopensDuringClosing(
        string map, string tag, string profile)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(map) is null, "Map is not converted.");
        using var run = new PlacedPlayer(map, PlaySide.Merc, SimLinkProfile.Get(profile));
        var door = run.Session.Host.Map.MapActors.OfType<Mover>().Single(m => m.Tag == tag);
        var copy = run.Session.Client.Map!.MapActors.OfType<Mover>().Single(m => m.Tag == tag);
        var detector = run.Session.Host.Map.MapActors.OfType<SPresenceTrigger>()
            .First(t => t.Event == (map == "SteeE" ? tag : "SM" + tag));
        FVector closed = door.Location;
        float moveTime = door.MoveTime;
        float stayOpenTime = door.StayOpenTime;
        door.bAlwaysRelevant = true;
        // Aquarius's merc detectors are centered in the closed brush. Stand beside it, at floor height,
        // within the authored cylinder, so placement does not depenetrate the pawn onto/away from the door.
        FVector occupied = detector.Location;
        if (map == "AquaE") occupied += tag == "Porte04" ? new FVector(100, 0, 54) : new FVector(0, -100, 54);
        // Place in the actual authored detector: collision creates and maintains the physical touch.
        run.Place(occupied, 0);
        run.Ticks(600);
        Assert.True(detector.Touching.Contains(run.Server), $"pawn={run.Server.Location}, detector={detector.Location}");
        Assert.True((door.Location - closed).Size() > 100);
        Assert.False(door.bInterpolating);
        Assert.InRange((copy.Location - door.Location).Size(), 0, 3);

        // Exiting via the normal placement path ends the overlap; wait until partway through the close.
        run.Place(closed + new FVector(600, 600, 0), 0);
        for (int i = 0; i < 900 && (!door.bInterpolating || door.bOpening); i++) run.Tick();
        Assert.True(door.bInterpolating && !door.bOpening);
        run.Ticks(20);
        run.Place(occupied, 0);
        run.Ticks(600);
        Assert.True((door.Location - closed).Size() > 100);
        Assert.False(door.bInterpolating);
        Assert.InRange((copy.Location - door.Location).Size(), 0, 3);
        Assert.Equal(moveTime, door.MoveTime);
        Assert.Equal(stayOpenTime, door.StayOpenTime);
    }

    [Fact]
    public void AquariusPumpDoorDoesNotRunAnIndependentClientCycleWhilePlayerPushesIt()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder("AquaE") is null, "Map is not converted.");
        using var run = new PlacedPlayer("AquaE", PlaySide.Spy);
        var door = run.Session.Host.Map.MapActors.OfType<Mover>().Single(m => m.Tag == "PorteCTF");
        var copy = run.Session.Client.Map!.MapActors.OfType<Mover>().Single(m => m.Tag == "PorteCTF");
        run.Place(new FVector(2623.3271f, 4261.0103f, -38.995003f), 16349);
        // This side requires the hack switch; start its completed authority event, then push through the cycle.
        door.eventTrigger(null, run.Server);
        FVector previous = copy.Location;
        float maxStep = 0;
        float travel = 0;
        run.Hold(["BT_Up"], 12, () =>
        {
            float step = (copy.Location - previous).Size();
            maxStep = System.Math.Max(maxStep, step);
            travel += step;
            previous = copy.Location;
            Assert.False(copy.IsInState("s_WaitAndRetry"), "Only the server may choose the retry leg.");
            Assert.Equal(0, copy.StateFrame.Pc);
        });
        Assert.True(travel > 50, $"Door did not animate: travel={travel}, server={door.Location}, client={copy.Location}");
        // A 120 uu leg over three seconds moves at most 1 uu/tick; allow network phase correction, but no key snap.
        Assert.InRange(maxStep, 0, 8);
    }

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

