using System;
using System.Collections.Generic;
using System.Linq;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Server.Hub;
using Xunit;

namespace OpenScct.Integration.Tests.Hub;

[Collection(BotMatchCollection.Name)]
public sealed class HubSnapshotFreshnessTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(NetProfile.Stock)]
    [InlineData(NetProfile.Enhanced)]
    public void NearbyVisitorsReceiveFreshSnapshots(NetProfile profile)
    {
        using var rig = new HubTests.Rig(4, settings: new HubSettings { Profile = profile });
        rig.Run(8);
        var ages = new List<double>();
        rig.Run(5, () =>
        {
            foreach (var bot in rig.Soak.Bots)
            {
                var session = bot.Session;
                if (session.PawnStates is not { LatestSnapshotTick: >= 0 } poses) continue;
                ages.Add(Math.Max(0, session.Driver.Timeline.RenderTimeFixed(session.LocalTick, 0) -
                    poses.LatestSnapshotTick * NetTimeline.Fraction) * (Sim.DeltaTime * 1000.0 / NetTimeline.Fraction));
            }
        });
        Assert.Equal(4, rig.Server.Players.Count);
        Assert.NotEmpty(ages);
        ages.Sort();
        double p95 = ages[(int)(ages.Count * .95)];
        output.WriteLine($"{profile}: p95 snapshot age {p95:0} ms, max {ages[^1]:0} ms; budget deferrals {rig.Server.Host.PawnStates.BudgetDeferrals}");
        Assert.True(p95 < 250, $"{profile}: p95 snapshot age {p95:0} ms, max {ages[^1]:0} ms");
    }

    [Fact]
    public void LastRemoteLeavingDoesNotLeaveAPermanentlyStaleSnapshot()
    {
        using var rig = new HubTests.Rig(0);
        using var first = new HubClient(rig.Network.Bind("first"), "server", rig.Now,
            new HubClientSettings { Maps = WalkerRig.Maps, Classes = WalkerRig.Classes, PlayerName = "First" });
        using var second = new HubClient(rig.Network.Bind("second"), "server", rig.Now,
            new HubClientSettings { Maps = WalkerRig.Maps, Classes = WalkerRig.Classes, PlayerName = "Second" });
        rig.Run(8, () => { first.Tick(rig.Now); second.Tick(rig.Now); });
        Assert.True(first.Session.PawnStates!.LatestSnapshotTick >= 0);
        second.Dispose();
        rig.Run(20, () => first.Tick(rig.Now));
        Assert.Single(rig.Server.Players);
        Assert.Equal(-1, first.Session.PawnStates.LatestSnapshotTick);
    }
}
