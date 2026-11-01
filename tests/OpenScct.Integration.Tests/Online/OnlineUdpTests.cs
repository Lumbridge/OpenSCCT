using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using OpenScct.Bots;
using OpenScct.Master;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

/// <summary>
/// Online play end to end over real UDP sockets on localhost, in real time: a master, a dedicated server listed on
/// it, and players who find the server in the list and join it directly, through a NAT punch or through the relay.
/// </summary>
public sealed class OnlineUdpTests
{
    private const string Key = "test-key";

    private static readonly NavCache Nav = new();

    private static bool TestBoxMissing => WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null;

    /// <summary>A dedicated server on a loopback UDP socket, listed on <paramref name="master"/>.</summary>
    private static (OnlineServer Server, LiteNetLibTransport Udp) Dedicated(MasterHost master, MasterLinkSettings? link = null,
        Func<OnlineServerSettings, OnlineServerSettings>? edit = null, IClock? clock = null)
    {
        LiteNetLibTransport udp = LiteNetLibTransport.Listen(0, 16, loopbackOnly: true);
        OnlineServerSettings settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Master = link ?? new MasterLinkSettings { Url = master.HttpAddress, InviteKey = Key },
        };
        if (edit is not null)
        {
            settings = edit(settings);
        }

        return (new OnlineServer(udp, settings, clock?.NowMicroseconds ?? 0, udp), udp);
    }

    private static ServerEntry WaitListed(OnlineRig rig, MasterClient browser, Func<ServerEntry, bool> ready)
    {
        ServerEntry? found = null;
        Task<ServerListResponse>? pending = null;
        string lastListing = "No completed listing request.";
        bool ok = rig.RunUntil(() =>
        {
            if (pending is { IsCompleted: true } done)
            {
                pending = null;
                lastListing = done.IsCompletedSuccessfully
                    ? string.Join("; ", done.Result.Servers.Select(e => $"{e.Id}: {e.Players} players, {e.Phase}"))
                    : done.Exception?.GetBaseException().Message ?? "Listing request canceled.";
                if (done.IsCompletedSuccessfully && done.Result.Servers.FirstOrDefault(ready) is { } entry)
                {
                    found = entry;
                    return true;
                }
            }

            pending ??= browser.ListAsync(protocol: rig.Server.Host.Driver.ProtocolHash, cancellationToken: TestContext.Current.CancellationToken);
            return false;
        }, 20);
        Assert.True(ok, "The server never showed up on the master as expected: " + rig.Server.Status() + "; last listing: " + lastListing);
        return found!;
    }

    private static OnlineClient JoinListed(OnlineRig rig, MasterClient browser, ServerEntry entry, OnlineClientSettings settings,
        ConnectPath path = ConnectPath.Auto)
    {
        Task<JoinResponse> join = browser.JoinAsync(entry.Id, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(rig.RunUntil(() => join.IsCompleted, 10));
        return rig.Add(new OnlineClient(JoinTarget.FromJoin(join.Result, path), settings, rig.Now));
    }

    [Fact]
    public async Task FourPlayersFindAListedServerPlayARoundAndGoBackToTheLobby()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        Nav.Prepare(WalkerRig.Maps, MapCatalog.TestBox, terminalsBlockPlayers: false);
        await using MasterHost master = await MasterHost.StartAsync(MasterTests.LocalOptions(Key), cancellationToken: ct);
        using var browser = new MasterClient(master.HttpAddress, Key);
        var clock = new WallClock();
        (OnlineServer server, LiteNetLibTransport udp) = Dedicated(master, clock: clock);
        using var rig = new OnlineRig(server, clock);
        var log = new List<string>();
        server.Logged += log.Add;

        // The master probes the server's port from a port it never sent to: on localhost the probe gets through.
        ServerEntry entry = WaitListed(rig, browser, e => e.Reachability == Reachability.Direct);
        Assert.Equal("Test server", entry.Name);
        Assert.Equal(MapCatalog.TestBox, entry.Map);
        Assert.Equal(udp.LocalPort, entry.Port);
        Assert.Equal(MasterApi.Hex(WalkerRig.Maps.FindMap(MapCatalog.TestBox)!.ContentHash), entry.Content);

        // The browser asks the server itself for its info (the ping column).
        using (var query = new ServerQuery(clock, loopbackOnly: true))
        {
            query.QueryServer(new IPEndPoint(IPAddress.Loopback, entry.Port), entry.Id);
            Assert.True(rig.RunUntil(() =>
            {
                query.Poll();
                return query.Results.Count > 0;
            }, 5));
            ServerQueryResult info = query.Results[0];
            Assert.Equal("Test server", info.Info.Name);
            Assert.Equal(SessionPhase.Lobby, info.Info.Phase);
            Assert.Equal(server.Host.Driver.ProtocolHash, info.Info.ProtocolHash);
            Assert.Equal(entry.Id, info.Tag);
        }

        // Two players and two bots join from the list: the bots are the spies and hack the terminals.
        rig.AfterServerTick = _ =>
        {
            OnlineRig.UnblockTerminals(server.Host.World);
            foreach (OnlineClient c in rig.Clients)
            {
                if (c.Session is { } s)
                {
                    OnlineRig.UnblockTerminals(s.World);
                }
            }
        };
        var players = new List<OnlineClient>();
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            players.Add(JoinListed(rig, browser, entry, OnlineRig.ClientSettings("SpyBot" + i, 0) with
            {
                SessionFactory = OnlineRig.BotSessions(BotTeam.Spy, () => new SpyBrain(SpyScript.HackShare(index)), Nav),
            }));
        }

        players.Add(JoinListed(rig, browser, entry, OnlineRig.ClientSettings("Merc A", 1)));
        players.Add(JoinListed(rig, browser, entry, OnlineRig.ClientSettings("Merc B", 1)));
        Assert.True(rig.RunUntil(() => players.All(p => p.State == OnlineClientState.Lobby && p.Lobby.Players.Count == 4), 30),
            Describe(rig, log));
        Assert.All(players, p => Assert.Equal(ConnectKind.Direct, p.ConnectedVia!.Kind));
        Assert.Equal(2, players[0].Lobby.Count(0));
        Assert.Equal(2, players[0].Lobby.Count(1));

        // Everyone gets ready; the dedicated lobby counts down and travels to the round.
        foreach (OnlineClient p in players)
        {
            p.SetReady(true);
        }

        Assert.True(rig.RunUntil(() => server.Phase == SessionPhase.Match && players.All(p => p.State == OnlineClientState.Match), 60),
            Describe(rig, log));

        // The spies take the objectives (TestBox's spies script ends in about half a minute).
        Assert.True(rig.RunUntil(() => players.All(p => p.Results is not null), 150), Describe(rig, log));
        MatchResults results = players[2].Results!;
        Assert.Equal(0, results.WinningTeam);
        Assert.Equal(0, results.WinningReason);
        Assert.Equal(4, results.Rows.Count);
        Assert.True(results.ObjectivesTaken >= results.ObjectivesNeeded && results.ObjectivesNeeded > 0);

        // The master shows the round's phase; then everyone asks for more, and the series being over, the lobby is next.
        foreach (OnlineClient p in players)
        {
            p.SetReady(true);
        }

        Assert.True(rig.RunUntil(() => server.Phase == SessionPhase.Lobby && players.All(p => p.State == OnlineClientState.Lobby &&
            p.Lobby.Players.Count == 4), 60), Describe(rig, log));
        Assert.Equal(1, server.RoundsStarted);

        // Leaving: the others see each player go, and the master's count follows.
        players[3].Leave();
        players[2].Leave();
        Assert.True(rig.RunUntil(() => server.Players.Count == 2 && players[0].Lobby.Players.Count == 2, 20), Describe(rig, log));
        WaitListed(rig, browser, e => e.Players == 2);
    }

    [Fact]
    public async Task ARelayOnlyHostIsJoinedThroughTheRelay()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        MasterOptions options = MasterTests.LocalOptions(Key);
        options.Probe = false;
        await using MasterHost master = await MasterHost.StartAsync(options, cancellationToken: ct);
        using var browser = new MasterClient(master.HttpAddress, Key);
        var clock = new WallClock();
        (OnlineServer server, LiteNetLibTransport udp) = Dedicated(master,
            new MasterLinkSettings { Url = master.HttpAddress, InviteKey = Key, NatPunch = false, Relay = true }, clock: clock);
        using var rig = new OnlineRig(server, clock);
        var log = new List<string>();
        server.Logged += log.Add;

        // No probe gets through and the host offers no punch: only the relay reaches it, as behind carrier-grade NAT.
        ServerEntry entry = WaitListed(rig, browser, e => e.Reachability == Reachability.RelayOnly);
        Assert.True(udp.RelayAcknowledged);

        // On localhost the players share the host's public address, so the automatic order would try the host's own
        // address first (as players behind the same router should); these two take the relay as remote players would.
        OnlineClient spy = JoinListed(rig, browser, entry, OnlineRig.ClientSettings("RelaySpy", 0), ConnectPath.Relay);
        OnlineClient merc = JoinListed(rig, browser, entry, OnlineRig.ClientSettings("RelayMerc", 1), ConnectPath.Relay);
        Assert.True(rig.RunUntil(() => spy.State == OnlineClientState.Lobby && merc.State == OnlineClientState.Lobby &&
            spy.Lobby.Players.Count == 2, 30), Describe(rig, log));
        Assert.Equal(ConnectKind.Relay, spy.ConnectedVia!.Kind);
        Assert.Equal(ConnectKind.Relay, merc.ConnectedVia!.Kind);
        Assert.StartsWith("relay ", server.Players[0].Address, StringComparison.Ordinal);

        // The round runs through the relay too (the travel keeps the relay session).
        spy.SetReady(true);
        merc.SetReady(true);
        Assert.True(rig.RunUntil(() => spy.State == OnlineClientState.Match && merc.State == OnlineClientState.Match &&
            spy.Session?.LocalPlayer?.Pawn is not null && merc.Session?.LocalPlayer?.Pawn is not null, 60), Describe(rig, log));
        long before = master.RelayForwarded;
        rig.RunUntil(() => false, 3);
        Assert.True(master.RelayForwarded - before > 200, $"relay forwarded {master.RelayForwarded - before} datagrams in 3 s");
        spy.Leave();
        merc.Leave();
        Assert.True(rig.RunUntil(() => server.Players.Count == 0, 20), Describe(rig, log));
    }

    [Fact]
    public async Task ANatPunchThroughTheIntroducerReachesTheHost()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using MasterHost master = await MasterHost.StartAsync(MasterTests.LocalOptions(Key), cancellationToken: ct);
        using var browser = new MasterClient(master.HttpAddress, Key);
        var clock = new WallClock();
        (OnlineServer server, _) = Dedicated(master, clock: clock);
        using var rig = new OnlineRig(server, clock);

        // Wait for the host's introducer registration (it comes with the second heartbeat's answer at the latest).
        Assert.True(rig.RunUntil(() => master.Registry.Snapshot().Any(r => r.NatExternal is not null), 20), server.Status());
        ServerEntry entry = WaitListed(rig, browser, _ => true);
        OnlineClient player = JoinListed(rig, browser, entry, OnlineRig.ClientSettings("Puncher", 0), ConnectPath.Intro);
        Assert.True(rig.RunUntil(() => player.State == OnlineClientState.Lobby, 20), string.Join(" / ", player.ConnectLog) + " " + player.Error);
        Assert.Equal(ConnectKind.Intro, player.ConnectedVia!.Kind);
        Assert.True(master.Introductions >= 1);
    }

    [Fact]
    public void LanSearchesFindTheServerAndBadAddressesFailReadably()
    {
        int lanPort = FreeUdpPort();
        var clock = new WallClock();
        using LiteNetLibTransport udp = LiteNetLibTransport.Listen(0, 8, loopbackOnly: true);
        var server = new OnlineServer(udp, OnlineRig.Settings(MapCatalog.TestBox) with { Name = "LAN party", LanPort = lanPort, LanLoopbackOnly = true },
            clock.NowMicroseconds, udp);
        using var rig = new OnlineRig(server, clock);
        Assert.NotNull(server.Lan);
        using var query = new ServerQuery(clock, loopbackOnly: true);
        query.SearchLan(lanPort);
        Assert.True(rig.RunUntil(() =>
        {
            query.Poll();
            return query.Results.Count > 0;
        }, 5));
        ServerQueryResult found = query.Results[0];
        Assert.True(found.FromLan);
        Assert.Equal("LAN party", found.Info.Name);
        Assert.Equal(udp.LocalPort, found.GameEndPoint.Port);

        // Joining what the LAN search found; then an address where nothing listens.
        OnlineClient lan = rig.Add(new OnlineClient(JoinTarget.FromAddress(LiteNetLibTransport.FormatEndPoint(found.GameEndPoint)),
            OnlineRig.ClientSettings("Lan", 0), rig.Now));
        OnlineClient nobody = rig.Add(new OnlineClient(JoinTarget.FromAddress("127.0.0.1:" + OnlineRig.Port(FreeUdpPort())),
            OnlineRig.ClientSettings("Nobody", 0) with { ConnectTimeoutSeconds = 2 }, rig.Now));
        Assert.True(rig.RunUntil(() => lan.State == OnlineClientState.Lobby && nobody.State == OnlineClientState.Failed, 15));
        Assert.StartsWith("Could not reach 127.0.0.1:", nobody.Error, StringComparison.Ordinal);
        Assert.Contains("no answer", nobody.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void JoinAttemptsFollowTheReachability()
    {
        var join = new JoinResponse
        {
            ServerId = "s", Direct = "203.0.113.7:28888", LanAddresses = ["192.168.1.20:28888"], Introducer = "198.51.100.1:28880",
            PunchToken = "00000000000000ab", Relay = "198.51.100.1:28881", RelayToken = "00000000000000cd",
        };
        static string Kinds(JoinTarget t) => string.Join(",", t.Attempts.Select(a => (a.IsLan ? "lan" : "") + a.Kind));
        Assert.Equal("Direct,Intro,Relay", Kinds(JoinTarget.FromJoin(join with { Reachability = Reachability.Direct })));
        Assert.Equal("Intro,Relay", Kinds(JoinTarget.FromJoin(join with { Reachability = Reachability.NeedsIntro })));
        Assert.Equal("Relay", Kinds(JoinTarget.FromJoin(join with { Reachability = Reachability.RelayOnly })));
        Assert.Equal("lanDirect,Direct,Relay", Kinds(JoinTarget.FromJoin(join with { Reachability = Reachability.RelayOnly, SameNetwork = true })));
        Assert.Equal("Relay", Kinds(JoinTarget.FromJoin(join with { Reachability = Reachability.Direct }, ConnectPath.Relay)));
        Assert.Equal("relay://198.51.100.1:28881/00000000000000cd", JoinTarget.FromJoin(join, ConnectPath.Relay).Attempts[0].Address);
        Assert.Equal("example.org:28888", JoinTarget.FromAddress("example.org").Attempts[0].Address);
        Assert.Equal("[::1]:28888", JoinTarget.FromAddress("::1").Attempts[0].Address);
        Assert.Equal("10.0.0.2:7777", JoinTarget.FromAddress("10.0.0.2:7777").Attempts[0].Address);
    }

    private static int FreeUdpPort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.Client.LocalEndPoint!).Port;
    }

    private static string Describe(OnlineRig rig, List<string> log) =>
        rig.Server.Status() + " | " + string.Join(" | ", rig.Clients.Select(c =>
            $"{c.Settings.PlayerName}: {c.State} {c.CurrentMap} {c.Error} players={c.Lobby.Players.Count} via={c.ConnectedVia?.Kind} [{string.Join("/", c.ConnectLog)}]")) +
        " | log: " + string.Join(" / ", log.TakeLast(12));
}
