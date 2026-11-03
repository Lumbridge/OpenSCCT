using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenScct.Master;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class MasterHeartbeatTests
{
    [Theory]
    [InlineData(Reachability.Direct, true)]
    [InlineData(Reachability.NeedsIntro, false)]
    [InlineData(Reachability.RelayOnly, true)]
    public async Task RoutineHostingInformationIsDiagnosticRatherThanAPlayerNotice(Reachability expectedReachability, bool listed)
    {
        const string serverKey = "trusted-server-key";
        var options = MasterTests.LocalOptions("key");
        options.Probe = expectedReachability == Reachability.Direct;
        options.TrustedServers.Add(new TrustedServerOptions { Name = "Test host", Key = serverKey });
        await using var master = await MasterHost.StartAsync(options, cancellationToken: TestContext.Current.CancellationToken);
        using var udp = LiteNetLibTransport.Listen(0, 4, loopbackOnly: true);
        udp.Unconnected = (from, data, _) =>
        {
            if (DiscoveryPackets.TypeOf(data) != DiscoveryPackets.Probe) return;
            var reply = new byte[DiscoveryPackets.HeaderBytes];
            int length = DiscoveryPackets.WriteHeader(reply, DiscoveryPackets.ProbeReply, DiscoveryPackets.NonceOf(data));
            udp.SendUnconnected(from, reply.AsSpan(0, length));
        };
        using var link = new MasterLink(new MasterLinkSettings
        {
            Url = master.HttpAddress, InviteKey = "key", ServerKey = serverKey, Listed = listed,
            NatPunch = expectedReachability == Reachability.NeedsIntro,
            Relay = expectedReachability == Reachability.RelayOnly,
        }, udp);
        var diagnostics = new List<string>();
        var notices = new List<string>();
        link.Diagnostic += diagnostics.Add;
        link.Notice += notices.Add;

        Assert.True(await TickUntilAsync(link, udp, () => link.Reachability == expectedReachability), link.LastError);

        Assert.True(link.Trusted);
        Assert.Contains(diagnostics, text => text.StartsWith(listed ? "Listed on the master server" : "Registered with the master server", StringComparison.Ordinal));
        Assert.Contains(diagnostics, text => text.StartsWith("The master server trusts this server:", StringComparison.Ordinal));
        Assert.Contains(diagnostics, text => text.StartsWith("Players reach this server ", StringComparison.Ordinal));
        Assert.Empty(notices);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateAndRegistrationFailureWarningsRemainPlayerNotices(bool registrationRefused)
    {
        const string newerVersion = "999.0.0";
        var options = MasterTests.LocalOptions("key");
        options.Probe = false;
        if (registrationRefused) options.MinVersion = newerVersion;
        else options.LatestVersion = newerVersion;
        await using var master = await MasterHost.StartAsync(options, cancellationToken: TestContext.Current.CancellationToken);
        using var udp = LiteNetLibTransport.Listen(0, 4, loopbackOnly: true);
        using var link = new MasterLink(new MasterLinkSettings { Url = master.HttpAddress, InviteKey = "key", NatPunch = false, Relay = false }, udp);
        var diagnostics = new List<string>();
        var notices = new List<string>();
        link.Diagnostic += diagnostics.Add;
        link.Notice += notices.Add;

        Assert.True(await TickUntilAsync(link, udp, () => notices.Count > 0));

        string warning = Assert.Single(notices);
        Assert.Contains(newerVersion, warning, StringComparison.Ordinal);
        Assert.StartsWith(registrationRefused ? "Master server: " : "Version ", warning, StringComparison.Ordinal);
        Assert.DoesNotContain(warning, diagnostics);
        Assert.Equal(!registrationRefused, link.Registered);
    }

    private static async Task<bool> TickUntilAsync(MasterLink link, LiteNetLibTransport udp, Func<bool> ready)
    {
        var info = new ServerInfo { Name = "Hosting notices", Map = "TestBox", GamePort = udp.LocalPort, MaxPlayers = 4 };
        for (int step = 0; step < 100; step++)
        {
            udp.Poll();
            link.Tick(step * 1_000_000L, () => info);
            if (ready()) return true;
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        return ready();
    }

    [Fact]
    public async Task RosterAndPhaseChangesDuringAHeartbeatArePublishedWithoutWaitingForThePeriodicInterval()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = MasterTests.LocalOptions("key");
        options.Probe = false;
        await using var master = await MasterHost.StartAsync(options, cancellationToken: ct);
        using var udp = LiteNetLibTransport.Listen(0, 4, loopbackOnly: true);
        using var link = new MasterLink(new MasterLinkSettings { Url = master.HttpAddress, InviteKey = "key", NatPunch = false, Relay = false }, udp);
        using var browser = new MasterClient(master.HttpAddress, "key");
        var info = new ServerInfo { Name = "Changing roster", Map = "TestBox", GamePort = udp.LocalPort, Mode = 5, Players = 1, MaxPlayers = 4 };
        link.Tick(0, () => info);
        // The first asynchronous heartbeat is still pending consumption on the host thread.
        info = info with { Players = 2, Phase = SessionPhase.Match };
        link.HeartbeatSoon();
        ServerEntry? entry = null;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            link.Tick(0, () => info); // No periodic interval elapses in this controlled host clock.
            await Task.Delay(100, ct);
            if (!link.Registered) continue;
            entry = Assert.Single((await browser.ListAsync(cancellationToken: ct)).Servers);
            if (entry.Players == 2 && entry.Phase == SessionPhase.Match) break;
        }
        Assert.NotNull(entry);
        Assert.Equal(2, entry.Players);
        Assert.Equal(SessionPhase.Match, entry.Phase);
    }
}
