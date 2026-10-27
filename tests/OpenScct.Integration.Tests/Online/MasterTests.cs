using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Master;
using OpenScct.Net.Discovery;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

/// <summary>The master's HTTP API on localhost: registration, the list and its filters, joins, keys and versions.</summary>
public sealed class MasterTests
{
    internal static MasterOptions LocalOptions(params string[] keys)
    {
        var options = new MasterOptions
        {
            HttpUrl = "http://127.0.0.1:0", PublicHost = "127.0.0.1", UdpBind = "127.0.0.1", IntroducerPort = 0, RelayPort = 0,
            ProbePort = 0, HeartbeatSeconds = 5, ExpirySeconds = 15, RegistrationSeconds = 15,
        };
        options.InviteKeys.AddRange(keys);
        return options;
    }

    private static HeartbeatRequest Beat(string name, ulong protocol, ulong content, int port = 40000) => new()
    {
        Name = name, GamePort = port, Map = "TestBox", Mode = 5, Players = 1, MaxPlayers = 4, Protocol = MasterApi.Hex(protocol),
        Content = MasterApi.Hex(content), Version = BuildInfo.Version,
    };

    [Fact]
    public async Task HostsRegisterAndBrowsersFilterTheList()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using MasterHost master = await MasterHost.StartAsync(LocalOptions("key"), cancellationToken: ct);
        using var client = new MasterClient(master.HttpAddress, "key");
        HeartbeatResponse a = await client.HeartbeatAsync(Beat("Alpha", 1, 10), cancellationToken: ct);
        HeartbeatResponse b = await client.HeartbeatAsync(Beat("Bravo", 2, 20, 40001), cancellationToken: ct);
        Assert.NotEqual(a.ServerId, b.ServerId);
        Assert.Equal("127.0.0.1", a.PublicAddress);
        Assert.Equal(master.IntroducerAddress, a.Introducer);
        Assert.Equal(master.RelayAddress, a.Relay);

        ServerListResponse all = await client.ListAsync(cancellationToken: ct);
        Assert.Equal(2, all.Servers.Count);
        ServerListResponse sameBuild = await client.ListAsync(protocol: 1, cancellationToken: ct);
        Assert.Equal("Alpha", Assert.Single(sameBuild.Servers).Name);
        ServerListResponse byContent = await client.ListAsync(content: [20UL, 99UL], cancellationToken: ct);
        Assert.Equal("Bravo", Assert.Single(byContent.Servers).Name);

        // A later heartbeat must carry the id and token; a wrong token is refused.
        HeartbeatResponse again = await client.HeartbeatAsync(Beat("Alpha", 1, 10) with { ServerId = a.ServerId, HostToken = a.HostToken, Players = 3 }, cancellationToken: ct);
        Assert.Equal(a.ServerId, again.ServerId);
        Assert.Equal(3, (await client.ListAsync(protocol: 1, cancellationToken: ct)).Servers[0].Players);
        MasterException wrong = await Assert.ThrowsAsync<MasterException>(() =>
            client.HeartbeatAsync(Beat("Alpha", 1, 10) with { ServerId = a.ServerId, HostToken = "1234" }, cancellationToken: ct));
        Assert.Equal(404, wrong.StatusCode);

        // Unlisted servers stay joinable but leave the list; unlisting removes the entry.
        await client.HeartbeatAsync(Beat("Bravo", 2, 20, 40001) with { ServerId = b.ServerId, HostToken = b.HostToken, Listed = false }, cancellationToken: ct);
        Assert.Single((await client.ListAsync(cancellationToken: ct)).Servers);
        JoinResponse join = await client.JoinAsync(b.ServerId, cancellationToken: ct);
        Assert.Equal("127.0.0.1:40001", join.Direct);
        Assert.True(join.SameNetwork);
        await client.UnlistAsync(a.ServerId, a.HostToken, cancellationToken: ct);
        Assert.Empty((await client.ListAsync(cancellationToken: ct)).Servers);
        MasterException gone = await Assert.ThrowsAsync<MasterException>(() => client.JoinAsync(a.ServerId, cancellationToken: ct));
        Assert.Equal("The server is no longer listed.", gone.Message);
    }

    [Fact]
    public async Task PrivateMastersNeedTheirKeyAndOldBuildsAreRefused()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        MasterOptions options = LocalOptions("secret");
        options.MinVersion = "9.0.0";
        options.Motd = "Welcome";
        await using MasterHost master = await MasterHost.StartAsync(options, cancellationToken: ct);
        using var none = new MasterClient(master.HttpAddress);
        using var bad = new MasterClient(master.HttpAddress, "guess");
        using var good = new MasterClient(master.HttpAddress, "secret");
        Assert.Equal(401, (await Assert.ThrowsAsync<MasterException>(() => none.ListAsync(cancellationToken: ct))).StatusCode);
        Assert.Equal(403, (await Assert.ThrowsAsync<MasterException>(() => bad.ListAsync(cancellationToken: ct))).StatusCode);
        Assert.Equal("Welcome", (await good.ListAsync(cancellationToken: ct)).Motd);

        MasterException old = await Assert.ThrowsAsync<MasterException>(() => good.HeartbeatAsync(Beat("Old", 1, 1), cancellationToken: ct));
        Assert.Equal(426, old.StatusCode);
        Assert.Contains("Please update", old.Message, StringComparison.Ordinal);

        // The status page is open (a health check) and says the master is private.
        MasterStatus status = await none.StatusAsync(cancellationToken: ct);
        Assert.True(status.InviteOnly);
        Assert.Equal(0, status.Servers);

        // Bad heartbeats are refused with a reason.
        options.MinVersion = null;
        MasterException invalid = await Assert.ThrowsAsync<MasterException>(() => good.HeartbeatAsync(Beat("", 1, 1), cancellationToken: ct));
        Assert.Equal(400, invalid.StatusCode);
        Assert.Contains("name", invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreachableMasterGivesAReadableError()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        // Windows retries a refused loopback connect for about two seconds before failing, so a short timeout can
        // win that race on a busy machine and give the "did not answer in time" message instead.
        using var client = new MasterClient("http://127.0.0.1:9", timeout: TimeSpan.FromSeconds(30));
        MasterException e = await Assert.ThrowsAsync<MasterException>(() => client.ListAsync(cancellationToken: ct));
        Assert.Equal(0, e.StatusCode);
        Assert.StartsWith("Could not reach the master server at 127.0.0.1", e.Message, StringComparison.Ordinal);
    }
}
