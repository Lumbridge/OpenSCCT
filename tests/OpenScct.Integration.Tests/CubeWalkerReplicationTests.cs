using System;
using System.Collections.Generic;
using System.Globalization;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Server;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>
/// The M0 check: a server and 4 clients, each moving its own walker for 10 s of sim time. Every client must see
/// all 4 walkers close to the server's truth at the client's render time (the interpolation delay behind).
/// <para>Two measures per sample:</para>
/// <list type="bullet">
/// <item>Displayed error: the interpolated location against the server truth at the render time.</item>
/// <item>Replication error: each replicated state against the server truth at the tick it is stamped with. Only
/// quantisation (1/16 uu) may separate them, on every profile.</item>
/// </list>
/// <para>With jitter or loss the server applies 0 or 2 moves on some ticks (it runs moves when they arrive, as
/// UE2 does), so its path has one-tick stalls of one move (about 7.3 uu) that interpolation between 20 Hz states
/// smooths over. That is why B and C use relaxed displayed-error tolerances.</para>
/// </summary>
public sealed class CubeWalkerReplicationTests
{
    private const int Clients = 4;
    private const int MeasureFrames = 600;
    private const float Tolerance = 5f;
    private const double BandwidthLimit = 12 * 1024;

    private readonly ITestOutputHelper _output;

    public CubeWalkerReplicationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Quantisation bound: half a 1/16 uu step on each axis.</summary>
    private const float ReplicationTolerance = 0.06f;

    private sealed record RunResult(ErrorStats Errors, ErrorStats Replication, double MaxBytesPerSecond,
        double MeanBytesPerSecond, List<string> Notes);

    private RunResult Run(WalkerRig rig, bool sendTexts = false)
    {
        rig.WaitForAllWalkers(Clients, input: WalkPatterns.For);

        // Let the walkers get up to speed and the timelines settle.
        for (int i = 0; i < 120; i++)
        {
            rig.Step(WalkPatterns.For);
        }

        var outBytes = new long[Clients];
        var outPackets = new long[Clients];
        for (int i = 0; i < Clients; i++)
        {
            NetConnection connection = rig.Server.Players[i].Connection;
            outBytes[i] = connection.Stats.OutBytes;
            outPackets[i] = connection.Stats.OutPackets;
        }

        var errors = new ErrorStats();
        var replication = new ErrorStats();
        var lastChecked = new Dictionary<ActorChannel, long>();
        int textsSent = 0;
        for (int frame = 0; frame < MeasureFrames; frame++)
        {
            rig.Step(WalkPatterns.For, sendTexts && frame % 10 == 0
                ? () => rig.Server.Broadcast("line " + (textsSent++).ToString(CultureInfo.InvariantCulture))
                : null);

            if (sendTexts && frame % 15 == 0)
            {
                for (int c = 0; c < Clients; c++)
                {
                    rig.Clients[c].SendText(FormattableString.Invariant($"c{c} {frame / 15}"));
                }
            }

            foreach (ClientSession client in rig.Clients)
            {
                Assert.Equal(Clients, WalkerRig.LiveWalkers(client));
                foreach (ActorChannel channel in client.ActorChannels)
                {
                    if (channel.Actor is not CubeWalker)
                    {
                        continue;
                    }

                    Assert.True(rig.TryGetTruthAt(channel.NetGuid, client.RenderTimeFixed, out FVector truth),
                        "No server truth for the client's render time.");
                    errors.Add(FVector.Dist(channel.Actor!.Location, truth));

                    ProxyMotion motion = channel.Motion!;
                    long stamped = channel.LastBunchTick;
                    if (!lastChecked.TryGetValue(channel, out long seen) || seen != stamped)
                    {
                        lastChecked[channel] = stamped;
                        Assert.True(rig.TryGetTruth(channel.NetGuid, stamped, out FVector atStamp));
                        replication.Add(FVector.Dist(motion.LatestLocation, atStamp));
                    }
                }
            }
        }

        double seconds = MeasureFrames / (double)Sim.Hz;
        double maxRate = 0;
        double sumRate = 0;
        for (int i = 0; i < Clients; i++)
        {
            NetConnection connection = rig.Server.Players[i].Connection;
            long bytes = connection.Stats.OutBytes - outBytes[i];
            long packets = connection.Stats.OutPackets - outPackets[i];
            double rate = (bytes + packets * NetConnection.PacketOverheadBytes) / seconds;
            maxRate = Math.Max(maxRate, rate);
            sumRate += rate;
        }

        var notes = new List<string>();
        if (sendTexts)
        {
            // Let the last lines through, then check every one arrived once and in order.
            for (int i = 0; i < 180; i++)
            {
                rig.Step(WalkPatterns.For);
            }

            foreach (ClientSession client in rig.Clients)
            {
                Assert.Equal(textsSent, client.ReceivedTexts.Count);
                for (int n = 0; n < textsSent; n++)
                {
                    Assert.Equal("line " + n.ToString(CultureInfo.InvariantCulture), client.ReceivedTexts[n]);
                }
            }

            int expectedPerClient = (MeasureFrames + 14) / 15;
            for (int c = 0; c < Clients; c++)
            {
                string prefix = FormattableString.Invariant($"c{c} ");
                int next = 0;
                foreach (string text in rig.Server.ReceivedTexts)
                {
                    if (text.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        Assert.Equal(prefix + next.ToString(CultureInfo.InvariantCulture), text);
                        next++;
                    }
                }

                Assert.Equal(expectedPerClient, next);
            }

            notes.Add(FormattableString.Invariant($"texts: {textsSent} server lines and {expectedPerClient} per client arrived in order"));
        }

        return new RunResult(errors, replication, maxRate, sumRate / Clients, notes);
    }

    private void Report(string name, WalkerRig rig, RunResult result)
    {
        _output.WriteLine(FormattableString.Invariant($"{name}: displayed error {result.Errors}"));
        _output.WriteLine(FormattableString.Invariant(
            $"{name}: within 5 uu {result.Errors.WithinPercent(Tolerance):F2}%, within 15 uu {result.Errors.WithinPercent(15f):F2}%"));
        _output.WriteLine(FormattableString.Invariant($"{name}: replication error {result.Replication}"));
        _output.WriteLine(FormattableString.Invariant(
            $"{name}: server to client {result.MeanBytesPerSecond:F0} B/s mean, {result.MaxBytesPerSecond:F0} B/s max (UDP/IP headers included)"));
        foreach (ClientSession client in rig.Clients)
        {
            NetStats s = client.Connection!.Stats;
            _output.WriteLine(FormattableString.Invariant(
                $"{name}: client {client.PlayerId} rtt {s.RttMicros / 1000.0:F1} ms jitter {s.JitterMicros / 1000.0:F1} ms in-lost {s.InPacketsLost} in-old {s.InPacketsOld} out-lost {s.OutPacketsLost} resends {s.OutReliableResends}"));
        }

        if (rig.SimLink is { } link)
        {
            _output.WriteLine(FormattableString.Invariant(
                $"{name}: simlink sent {link.Sent} lost {link.Lost} duplicated {link.Duplicated} reordered {link.Reordered}"));
        }

        foreach (string note in result.Notes)
        {
            _output.WriteLine(name + ": " + note);
        }
    }

    [Theory]
    [InlineData(K.NetServerMaxTickRate)]
    [InlineData(K.LanServerMaxTickRate)]
    [InlineData(60)]
    public void FourLoopbackWalkersReplicate(int netRate)
    {
        using WalkerRig rig = WalkerRig.Create(null, 0, Clients, new ServerSettings { NetRate = netRate });
        RunResult result = Run(rig);
        Report("loopback " + netRate.ToString(CultureInfo.InvariantCulture) + " Hz", rig, result);
        Assert.All(rig.Clients, c => Assert.Equal(netRate, c.ServerNetRate));

        Assert.Equal(Clients * Clients * MeasureFrames, result.Errors.Count);
        Assert.True(result.Errors.Max <= Tolerance, $"Max error {result.Errors.Max} uu is above {Tolerance} uu.");
        Assert.True(result.Replication.Max <= ReplicationTolerance, $"Replication error {result.Replication}");
        Assert.True(result.MaxBytesPerSecond < BandwidthLimit, $"{result.MaxBytesPerSecond} B/s per client is over 12 KB/s.");
    }

    [Theory]
    [InlineData("A", 101UL)]
    [InlineData("B", 202UL)]
    [InlineData("C", 303UL)]
    public void FourWalkersReplicateOverSimLink(string profileName, ulong seed)
    {
        SimLinkProfile profile = SimLinkProfile.Get(profileName);
        using WalkerRig rig = WalkerRig.Create(profile, seed, Clients);
        RunResult result = Run(rig, sendTexts: true);
        Report("simlink " + profileName, rig, result);

        // Relaxed with loss and jitter (see the class remarks): share within 5 uu, share within two moves
        // (15 uu), and the largest error.
        (double within5, double within15, float max) = profileName switch
        {
            "A" => (100.0, 100.0, Tolerance),
            "B" => (98.0, 100.0, 20f),
            _ => (85.0, 99.5, 25f),
        };

        Assert.True(result.Errors.WithinPercent(Tolerance) >= within5,
            $"Only {result.Errors.WithinPercent(Tolerance):F2}% of samples within {Tolerance} uu (need {within5}%). {result.Errors}");
        Assert.True(result.Errors.WithinPercent(15f) >= within15,
            $"Only {result.Errors.WithinPercent(15f):F2}% of samples within 15 uu (need {within15}%). {result.Errors}");
        Assert.True(result.Errors.Max <= max, $"Max error {result.Errors.Max} uu is above {max} uu. {result.Errors}");
        Assert.True(result.Replication.Max <= ReplicationTolerance, $"Replication error {result.Replication}");
        Assert.True(result.MaxBytesPerSecond < BandwidthLimit, $"{result.MaxBytesPerSecond} B/s per client is over 12 KB/s.");
    }

    [Fact]
    public void ProfileDStillConverges()
    {
        using WalkerRig rig = WalkerRig.Create(SimLinkProfile.D, 404UL, Clients);
        RunResult result = Run(rig, sendTexts: true);
        Report("simlink D", rig, result);
        Assert.True(result.Errors.WithinPercent(Tolerance) >= 75.0, result.Errors.ToString());
        Assert.True(result.Replication.Max <= ReplicationTolerance, $"Replication error {result.Replication}");
    }

    [Fact]
    public void SameSeedGivesTheSameRun()
    {
        static ulong RunOnce()
        {
            using WalkerRig rig = WalkerRig.Create(SimLinkProfile.C, 77UL, Clients);
            ulong hash = NetHash.OffsetBasis;
            for (int frame = 0; frame < 900; frame++)
            {
                rig.Step(WalkPatterns.For);
                foreach (ClientSession client in rig.Clients)
                {
                    foreach (ActorChannel channel in client.ActorChannels)
                    {
                        if (channel.Actor is not CubeWalker)
                        {
                            continue;
                        }

                        FVector l = channel.Actor.Location;
                        hash = Mix(hash, BitConverter.SingleToUInt32Bits(l.X));
                        hash = Mix(hash, BitConverter.SingleToUInt32Bits(l.Y));
                    }
                }
            }

            return hash;
        }

        static ulong Mix(ulong h, uint v) => (h ^ v) * 1099511628211UL;

        Assert.Equal(RunOnce(), RunOnce());
    }
}
