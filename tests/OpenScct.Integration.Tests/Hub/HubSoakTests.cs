using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using OpenScct.Bots;
using OpenScct.Server.Hub;
using Xunit;

namespace OpenScct.Integration.Tests.Hub;

/// <summary>
/// The hub's short soak (docs/HUB.md): 50 bot visitors on one hub server over SimLink with a virtual clock, joining
/// ten a second, wandering the whole hall, chatting and changing side. Every bot must be in and playing, the server
/// must keep the match running, and the mean download per client must stay near a normal four-player match's
/// (docs/NETCODE.md "Bot matches": 1.1 to 2.3 KB/s) thanks to the interest layer. The long soak over real UDP is
/// <c>svm-server --hub</c> with <c>--hub-bots</c> in a second process.
/// </summary>
[Collection(BotMatchCollection.Name)]
public sealed class HubSoakTests
{
    /// <summary>Mean download per client the short soak may use (bytes per second, packet overhead included).</summary>
    private const double MeanDownLimit = 5 * 1024;

    private readonly ITestOutputHelper _output;

    public HubSoakTests(ITestOutputHelper output) => _output = output;

    private static int Seconds => int.TryParse(Environment.GetEnvironmentVariable("SVM_HUB_SOAK_SECONDS"), NumberStyles.Integer,
        CultureInfo.InvariantCulture, out int s) && s > 0 ? s : 15;

    [Fact]
    public void FiftyVisitorsStayInWithinTheBandwidthBudget()
    {
        using var rig = new HubTests.Rig(50, "A", seed: 9);
        rig.Server.Host.PlayerLeft += slot => _output.WriteLine($"left {slot.Name}: {slot.Connection.CloseReason}");
        var ticks = new List<double>();
        var watch = Stopwatch.StartNew();
        long measureFrom = 0;
        long downAtMeasure = 0;
        rig.Run(Seconds, () =>
        {
            if (rig.Frame == 8 * 60)
            {
                // Bandwidth counts from 8 s on, once the crowd is in (the joins send every actor's first bunch).
                measureFrom = rig.Frame;
                downAtMeasure = ServerOutBytes(rig.Server);
            }
        });
        double wall = watch.Elapsed.TotalSeconds;
        HubSoakReport report = rig.Soak.Report(Seconds);
        double measured = (rig.Frame - measureFrom) / 60.0;
        double steadyDown = (ServerOutBytes(rig.Server) - downAtMeasure) / measured / Math.Max(1, rig.Server.Players.Count);
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{Seconds} s simulated in {wall:F1} s; {rig.Server.Status()}; chat said {rig.Soak.ChatSaid}, side requests {rig.Soak.SideRequests}"));
        _output.WriteLine(report.ToString());
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"steady state down per client (server side) {steadyDown:F0} B/s"));
        _output.WriteLine($"pawn snapshots {rig.Server.Host.PawnStates.SnapshotsSent}, transitions {rig.Server.Host.PawnStates.AnimationRecords}, resyncs {rig.Server.Host.PawnStates.HistoryResyncs}, budget deferrals {rig.Server.Host.PawnStates.BudgetDeferrals}");
        var first = rig.Server.Players[0].Slot.Connection;
        _output.WriteLine($"control bytes={first.ControlChannel?.PayloadBytesSent} total bytes={first.Stats.OutBytes}");
        foreach (var state in rig.Server.Host.Driver.ActorStates)
            foreach (var channel in state.Channels)
                if (ReferenceEquals(channel.Connection, first) && rig.Server.Host.Driver.IsNetOwner(state.Actor, first))
                    _output.WriteLine($"owner {state.Actor.GetType().Name}: bytes={channel.PayloadBytesSent} initial={channel.SentInitial}/{channel.OpenAcked} dirty={channel.HasDirtyProperties} updates={channel.PropertyBunchesSent}");
        var sorted = new List<double>(rig.ServerTickMs);
        sorted.Sort();
        OpenScct.Server.PawnProxySender proxies = rig.Server.Host.ProxySender!;
        OpenScct.Net.NetDriverStats ds = rig.Server.Host.Driver.Stats;
        _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"server tick p50 {sorted[sorted.Count / 2]:F2} ms p99 {sorted[(int)(sorted.Count * 0.99)]:F2} ms max {sorted[^1]:F2} ms; bots {rig.BotSeconds:F1} s; samples {proxies.Samples} batches {proxies.ActionBatches} rotation pushes {proxies.RotationPushes}; channels opened {ds.ChannelsOpened} closed {ds.ChannelsClosedIrrelevant}, calls sent {ds.CallsSent}"));
        foreach (HubBotReport b in report.Bots)
        {
            _output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {b.Name} team {b.Team}: down {b.DownBytesPerSecond:F0} up {b.UpBytesPerSecond:F0} B/s, sees {b.PawnsSeen}, corrections {b.CorrectionsApplied}, chat {b.ChatLines}, walks {b.Arrivals}"));
        }

        Assert.Equal(50, rig.Server.Players.Count);
        Assert.Equal(50, report.Joined);
        Assert.False(rig.Server.Game.bGameEnded);
        Assert.True(steadyDown < MeanDownLimit, $"steady state down {steadyDown:F0} B/s per client");
        Assert.True(rig.Server.Host.Driver.Stats.InterestDeferred > 0);
        int seen = 0;
        foreach (HubBotReport visitor in report.Bots) seen += visitor.PawnsSeen;
        Assert.True(seen >= report.Joined * 2, $"crowd budget starved pawn initialization: {seen} total peer views for {report.Joined} visitors");
    }

    private static long ServerOutBytes(HubServer server)
    {
        long sum = 0;
        foreach (HubPlayer p in server.Players)
        {
            sum += p.Slot.Connection.Stats.OutBytes + p.Slot.Connection.Stats.OutPackets * OpenScct.Net.NetConnection.PacketOverheadBytes;
        }

        return sum;
    }
}
