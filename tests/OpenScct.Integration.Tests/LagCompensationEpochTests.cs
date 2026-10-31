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
