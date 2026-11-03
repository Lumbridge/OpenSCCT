using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Game;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class PresenceDetectorPopulationTests
{
    [Fact]
    public void EveryMovingSpyGetsOneWarningEvenBeyondTheOriginalTwoSlots()
    {
        var scene = GadgetKit.Scene();
        var net = new RecordingGameHost();
        scene.Level.SetService<IGameHost>(net);
        var spies = new SBasePawn[4];
        for (int i = 0; i < spies.Length; i++)
        {
            var (pc, pawn) = scene.Player(true, new FVector((i - 1.5f) * 100f, 200f, 100f));
            pc.Player = new RemoteConnection();
            spies[i] = pawn;
        }
        var detector = scene.Level.SpawnActor(new SPresDetector { DetectionPeriod = 0f }, new FVector(0f, 0f, 100f), default)!;
        foreach (SBasePawn spy in spies) spy.Velocity = new FVector(100f, 0f, 0f);
        scene.World.Step();
        Assert.Equal(4, detector.DetectedPawns.Count);
        foreach (SBasePawn spy in spies)
            Assert.Single(net.Calls, call => ReferenceEquals(call.Target, spy.Controller) && call.Call.Function == new Name("AddMessageOnHud"));

        scene.World.Step();
        Assert.Equal(4, net.Calls.Count(call => call.Call.Function == new Name("AddMessageOnHud")));
        spies[0].Velocity = default;
        scene.World.Step();
        Assert.DoesNotContain(spies[0], detector.DetectedPawns);
        Assert.Equal(4, net.Calls.Count(call => call.Call.Function == new Name("AddMessageOnHud"))); // Slot movement is not a new spy.
        spies[0].Velocity = new FVector(100f, 0f, 0f);
        scene.World.Step();
        Assert.Equal(5, net.Calls.Count(call => call.Call.Function == new Name("AddMessageOnHud")));

        foreach (SBasePawn spy in spies) spy.Velocity = default;
        scene.Run(detector.fWarningDuration + 0.1f);
        Assert.False(detector.bDetected);
        Assert.Empty(detector.DetectedPawns);
        Assert.All(detector.DetectedPawn, Assert.Null);
    }

    [Fact]
    public void DeadSpiesAndChaffCannotStartADetection()
    {
        var scene = GadgetKit.Scene();
        (_, SBasePawn spy) = scene.Player(true, new FVector(100f, 0f, 100f));
        spy.Velocity = new FVector(100f, 0f, 0f);
        var detector = scene.Level.SpawnActor(new SPresDetector(), new FVector(0f, 0f, 100f), default)!;
        spy.Health = 0;
        Assert.False(detector.ScanPresence());
        spy.Health = 100;
        detector.bChaffed = true;
        Assert.False(detector.ScanPresence());
        detector.bChaffed = false;
        Assert.True(detector.ScanPresence());
    }

    private sealed class RemoteConnection : UNetConnection { }
}
