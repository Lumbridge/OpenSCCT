using System.Threading.Tasks;
using OpenScct.Master;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class MasterHeartbeatTests
{
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
