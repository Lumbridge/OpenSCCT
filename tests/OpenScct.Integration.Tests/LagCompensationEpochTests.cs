using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

public sealed class LagCompensationEpochTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(1500)]
    public void MovingTargetRewindUsesServerTimeInsideAnOwnerCommandClock(long inputTick)
    {
        var world = new World(21);
        var pawn = world.Level.Spawn<SPawnAttaque>()!;
        var shooter = world.Level.Spawn<SPAWNDEFENSE>()!;
        var lag = LagCompensation.Install(world.Level);
        lag.Enabled = true;
        for (int i = 0; i < 1000; i++)
        {
            world.Step();
            pawn.Location = new FVector(500, (i - 990) * 10, 0);
            lag.Record();
        }

        // The client saw tick 991, where the spy crossed y=0; now the spy is 90 uu past the crosshair.
        var start = new FVector(0, 0, 0);
        var end = new FVector(1000, 0, 0);
        using (world.Level.CommandTime(inputTick, inputTick * Sim.DeltaTime, Sim.DeltaTime))
        using (world.Level.CommandTime(inputTick + 1, (inputTick + 1) * Sim.DeltaTime, Sim.DeltaTime))
        {
            lag.BeginShot(991 * NetTimeline.Fraction);
            try
            {
                Assert.Equal(150f, lag.LastRewindMs);
                Assert.Same(pawn, TraceHitZones.Instance.TraceBone(shooter, end, start, true).Actor);
            }
            finally { lag.EndShot(); }
        }
        Assert.Equal(1000, world.Level.TickCount);
        Assert.NotSame(pawn, TraceHitZones.Instance.TraceBone(shooter, end, start, true).Actor);
    }

    [Fact]
    public void ShotTimeRemainsTheDisplayedFrameWhileAdaptiveSimulationAdvances()
    {
        using var rig = WalkerRig.Create(SimLinkProfile.Ping200, 125, 1);
        for (int i = 0; i < 180; i++) rig.Step();
        ClientSession client = rig.Clients[0];
        long previous = client.RenderTimeFixed;
        rig.Step();
        long current = client.RenderTimeFixed;
        client.ReportRenderedFrame(0.25f);
        long shown = previous + (long)((current - previous) * 0.25f);
        Assert.Equal(shown, client.ViewTimeFixed);
        for (int i = 0; i < 8; i++) rig.Step();
        Assert.True(client.RenderTimeFixed > current);
        Assert.Equal(shown, client.ViewTimeFixed);
        client.ReportRenderedFrame(1f);
        Assert.Equal(client.RenderTimeFixed, client.ViewTimeFixed);
    }

    [Fact]
    public void RewindCannotCrossAShortResetOrTeleportAndRejectsFutureTime()
    {
        var world = new World(9);
        var pawn = world.Level.Spawn<SPawnAttaque>()!;
        var lag = LagCompensation.Install(world.Level);
        lag.Enabled = true;
        void Tick() { world.Step(); lag.Record(); }
        for (int i = 0; i < 40; i++) { pawn.Location = new FVector(100 + i, 0, 0); Tick(); }
        var pills = new List<PillShape>();
        lag.BeginShot(30 * NetTimeline.Fraction);
        Assert.True(lag.TryGetPills(pawn, pills, out _));
        lag.EndShot();
        pawn.ResetNetworkHistory();
        for (int i = 0; i < 6; i++) Tick();
        lag.BeginShot(35 * NetTimeline.Fraction);
        Assert.False(lag.TryGetPills(pawn, pills, out _));
        lag.EndShot();
        pawn.Location = new FVector(2000, 0, 0);
        for (int i = 0; i < 6; i++) Tick();
        lag.BeginShot(46 * NetTimeline.Fraction);
        Assert.False(lag.TryGetPills(pawn, pills, out _));
        lag.EndShot();
        long rewound = lag.RewoundShots;
        lag.BeginShot((world.TickCount + 1) * NetTimeline.Fraction);
        Assert.False(lag.TryGetPills(pawn, pills, out _));
        Assert.Equal(rewound, lag.RewoundShots);
    }

    [Fact]
    public void DefaultCeilingAcceptsThreeHundredMillisecondsAndBoundsOlderShots()
    {
        var world = new World(10);
        var pawn = world.Level.Spawn<SPawnAttaque>()!;
        var lag = LagCompensation.Install(world.Level);
        lag.Enabled = true;
        Assert.Equal(300, lag.CeilingMs);
        for (int i = 0; i < 60; i++) { world.Step(); lag.Record(); }
        lag.BeginShot(30 * NetTimeline.Fraction);
        Assert.Equal(300f, lag.LastRewindMs);
        Assert.True(lag.TryGetPills(pawn, new List<PillShape>(), out _));
    }
}
