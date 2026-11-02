using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

/// <summary>
/// The online session's lobby, travel, round, results and leaving over SimLink on a virtual clock: fast and
/// deterministic, the same code as over UDP.
/// </summary>
public sealed class LobbyTests
{
    private static (OnlineRig Rig, SimLinkNetwork Network) Create(OnlineServerSettings settings, SimLinkProfile profile, ulong seed = 7)
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, seed, profile);
        var server = new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds);
        return (new OnlineRig(server, clock), network);
    }

    private static OnlineClient Join(OnlineRig rig, SimLinkNetwork network, string name, int team, Func<OnlineClientSettings, OnlineClientSettings>? edit = null)
    {
        int n = 0;
        OnlineClientSettings settings = OnlineRig.ClientSettings(name, team);
        if (edit is not null)
        {
            settings = edit(settings);
        }

        var target = JoinTarget.Custom("Test server", () => network.Bind(name + "-" + (n++).ToString(CultureInfo.InvariantCulture)), "server");
        return rig.Add(new OnlineClient(target, settings, rig.Now));
    }

    private static bool TestBoxMissing => WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null;

    [Fact]
    public void DefaultLaunchTravelsWithoutAFiveSecondCountdown()
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Dedicated = false, HostKey = "k", AutoLaunch = false,
            LaunchCountdownSeconds = new OnlineServerSettings().LaunchCountdownSeconds,
        };
        (OnlineRig rig, SimLinkNetwork network) = Create(settings, SimLinkProfile.A);
        using (rig)
        {
            OnlineClient host = Join(rig, network, "Host", 1, s => s with { HostKey = "k" });
            Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Lobby &&
                host.Lobby.Local is { Map: MapState.Ok }, 10), Describe(rig));
            host.Launch();
            Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Travelling ||
                host.CurrentMap == MapCatalog.TestBox, .5), Describe(rig));
        }
    }

    [Fact]
    public void RoundWaitsForBothRenderedScenesBeforeSpawningPlayers()
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Dedicated = false, HostKey = "k", AutoLaunch = false,
        };
        (OnlineRig rig, SimLinkNetwork network) = Create(settings, SimLinkProfile.A);
        using (rig)
        {
            OnlineClient host = Join(rig, network, "Host", 0, s => s with { HostKey = "k", AutoReportReady = false });
            OnlineClient laptop = Join(rig, network, "Laptop", 1, s => s with { AutoReportReady = false });
            Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Lobby && laptop.State == OnlineClientState.Lobby &&
                host.Lobby.Players.Count == 2, 10), Describe(rig));
            laptop.SetReady(true);
            Assert.True(rig.RunUntil(() => host.Lobby.Players.All(p => p.Name == "Host" || p.Ready), 5), Describe(rig));
            host.Launch();
            Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Loading && laptop.State == OnlineClientState.Loading &&
                host.Session?.LocalPlayer is not null && laptop.Session?.LocalPlayer is not null, 15), Describe(rig));
            host.Session!.MarkReadyToPlay();
            rig.RunUntil(() => false, 10);
            Assert.True(host.Session.ReportedWaiting);
            Assert.False(laptop.Session!.ReportedWaiting);
            Assert.Equal(SessionPhase.Loading, rig.Server.Phase);
            Assert.False(rig.Server.Host.StartGate!.Started);
            Assert.Null(host.Session.LocalPlayer!.Pawn);
            laptop.Session.MarkReadyToPlay();
            Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Match && laptop.State == OnlineClientState.Match &&
                host.Session.LocalPlayer.Pawn is not null && laptop.Session.LocalPlayer!.Pawn is not null, 10), Describe(rig));
        }
    }

    [Fact]
    public void SceneLoadingCanExceedTheNormalIdleTimeoutButReadyPlayersStillTimeOut()
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Dedicated = false, HostKey = "k", AutoLaunch = false, LoadTimeoutSeconds = 45,
        };
        (OnlineRig rig, SimLinkNetwork network) = Create(settings, SimLinkProfile.A);
        using (rig)
        {
            OnlineClient host = Join(rig, network, "Host", 0, s => s with { HostKey = "k", AutoReportReady = false });
            OnlineClient laptop = Join(rig, network, "Laptop", 1, s => s with { AutoReportReady = false });
            Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Lobby && laptop.State == OnlineClientState.Lobby && host.Lobby.Players.Count == 2, 10), Describe(rig));
            laptop.SetReady(true);
            Assert.True(rig.RunUntil(() => host.Lobby.Players.All(p => p.Name == "Host" || p.Ready), 5), Describe(rig));
            host.Launch();
            Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Loading && laptop.State == OnlineClientState.Loading &&
                host.Session?.LocalPlayer is not null && laptop.Session?.LocalPlayer is not null, 15), Describe(rig));
            host.Session!.MarkReadyToPlay();
            rig.ShouldTickClient = c => c != laptop;
            rig.RunUntil(() => false, 20);
            Assert.Contains(rig.Server.Host.Players, p => p.Name == "Laptop");
            Assert.False(rig.Server.Host.StartGate!.Started);
            rig.ShouldTickClient = null;
            laptop.Session!.MarkReadyToPlay();
            Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Match && laptop.State == OnlineClientState.Match &&
                laptop.Session.LocalPlayer!.Pawn is not null, 10), Describe(rig));
            rig.ShouldTickClient = c => c != laptop;
            rig.RunUntil(() => false, 17);
            Assert.DoesNotContain(rig.Server.Host.Players, p => p.Name == "Laptop");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConnectedClientThatNeverFinishesItsSceneLoadTimesOutWithoutBlockingReadyPlayers(bool keepsPumpingNetwork)
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Dedicated = false, HostKey = "k", AutoLaunch = false, LoadTimeoutSeconds = 20,
        };
        (OnlineRig rig, SimLinkNetwork network) = Create(settings, SimLinkProfile.A);
        using (rig)
        {
            OnlineClient host = Join(rig, network, "Host", 0, s => s with { HostKey = "k", AutoReportReady = false });
            OnlineClient stuck = Join(rig, network, "Stuck", 1, s => s with { AutoReportReady = false });
            Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Lobby && stuck.State == OnlineClientState.Lobby &&
                host.Lobby.Players.Count == 2, 10), Describe(rig));
            stuck.SetReady(true);
            Assert.True(rig.RunUntil(() => host.Lobby.Players.All(p => p.Name == "Host" || p.Ready), 5), Describe(rig));
            host.Launch();
            Assert.True(rig.RunUntil(() => host.Session?.LocalPlayer is not null && stuck.Session?.LocalPlayer is not null, 15), Describe(rig));
            host.Session!.MarkReadyToPlay();
            if (!keepsPumpingNetwork) rig.ShouldTickClient = c => c != stuck;
            Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Match && host.Session.LocalPlayer!.Pawn is not null, 30), Describe(rig));
            Assert.DoesNotContain(rig.Server.Players, p => p.Name == "Stuck");
            Assert.Equal(1, rig.Server.Host.StartGate!.PlayersToStart);
        }
    }

    [Fact]
    public void PlayersMeetInTheLobbyPickSidesAndLaunchARound()
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        (OnlineRig rig, SimLinkNetwork network) = Create(OnlineRig.Settings(MapCatalog.TestBox, timeLimit: 1), SimLinkProfile.B);
        using (rig)
        {
            OnlineClient spy = Join(rig, network, "Sam", 0);
            OnlineClient merc = Join(rig, network, "Shadownet", 0);
            Assert.True(rig.RunUntil(() => spy.State == OnlineClientState.Lobby && merc.State == OnlineClientState.Lobby &&
                spy.Lobby.Players.Count == 2 && merc.Lobby.Players.Count == 2, 15), Describe(rig));

            // Both asked for the spies; the second moves to the mercs. Sides and map checks reach everyone.
            merc.SetTeam(1);
            Assert.True(rig.RunUntil(() => spy.Lobby.Players.Any(p => p.Name == "Shadownet" && p.Team == 1) &&
                spy.Lobby.Players.All(p => p.Map == MapState.Ok), 5), Describe(rig));
            Assert.Equal("Test server", spy.Lobby.ServerName);
            Assert.Equal(MapCatalog.TestBox, spy.Lobby.Setup.Map);
            Assert.Equal(1, spy.Lobby.Setup.TimeLimit);

            // Nobody launches before everyone is ready.
            spy.SetReady(true);
            rig.RunUntil(() => false, 2);
            Assert.Equal(SessionPhase.Lobby, rig.Server.Phase);
            merc.SetReady(true);
            Assert.True(rig.RunUntil(() => rig.Server.Phase == SessionPhase.Match, 30), Describe(rig));
            Assert.True(rig.RunUntil(() => spy.State == OnlineClientState.Match && merc.State == OnlineClientState.Match &&
                spy.Session?.LocalPlayer?.Pawn is not null && merc.Session?.LocalPlayer?.Pawn is not null, 15), Describe(rig));
            Assert.Equal(MapCatalog.TestBox, spy.CurrentMap);
            Assert.Equal(1, rig.Server.RoundsStarted);

            // The server logged each player in on its lobby side.
            Assert.Equal(0, rig.Server.Players.Single(p => p.Name == "Sam").Slot!.Controller!.PlayerReplicationInfo!.Team!.TeamIndex);
            Assert.Equal(1, rig.Server.Players.Single(p => p.Name == "Shadownet").Slot!.Controller!.PlayerReplicationInfo!.Team!.TeamIndex);

            // The one-minute round runs out (mercs win on time), the end screen passes, the results arrive.
            Assert.True(rig.RunUntil(() => spy.Results is not null && merc.Results is not null, 90), Describe(rig));
            Assert.Equal(1, spy.Results!.WinningTeam);
            Assert.Equal(1, spy.Results.WinningReason);
            Assert.Equal(2, spy.Results.Rows.Count);
            Assert.Equal(OnlineClientState.Results, merc.State);

            // Both ask for the next round: the series is over, so the dedicated server goes back to its lobby.
            spy.SetReady(true);
            merc.SetReady(true);
            Assert.True(rig.RunUntil(() => spy.State == OnlineClientState.Lobby && merc.State == OnlineClientState.Lobby &&
                rig.Server.Phase == SessionPhase.Lobby && spy.Lobby.Players.Count == 2, 30), Describe(rig));

            // One leaves; the other sees it go.
            merc.Leave();
            Assert.True(rig.RunUntil(() => spy.Lobby.Players.Count == 1 && rig.Server.Players.Count == 1, 20), Describe(rig));
            Assert.Equal(OnlineClientState.Left, merc.State);
        }
    }

    [Fact]
    public void LaunchWaitsForTheMapAndNamesWhoLacksIt()
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        (OnlineRig rig, SimLinkNetwork network) = Create(OnlineRig.Settings(MapCatalog.TestBox) with { Dedicated = false, HostKey = "k" },
            SimLinkProfile.A);
        using (rig)
        {
            // The host's own player, and a player whose TestBox is a different build.
            OnlineClient host = Join(rig, network, "Host", 0, s => s with { HostKey = "k" });
            var other = new MapCatalog();
            other.Register(MapCatalog.TestBox, (world, client) =>
                new MapInfo(MapCatalog.TestBox, "TestBox/manifest.json", 12345, "x", [], [new MapStart(default, default, 0)], []));
            OnlineClient guest = Join(rig, network, "Guest", 1, s => s with { Maps = other });
            Assert.True(rig.RunUntil(() => host.Lobby.Players.Any(p => p.Name == "Guest" && p.Map == MapState.Differs), 15), Describe(rig));
            Assert.True(host.Lobby.IsHost);
            Assert.False(guest.Lobby.IsHost);

            guest.SetReady(true);
            Assert.True(rig.RunUntil(() => host.Lobby.Players.Any(p => p.Name == "Guest" && p.Ready), 5));
            host.Launch();
            Assert.True(rig.RunUntil(() => host.Lobby.Messages.Any(m => m.Text.Contains("different version of TestBox", StringComparison.Ordinal)), 5),
                string.Join(" | ", host.Lobby.Messages.Select(m => m.Text)));
            Assert.Equal(SessionPhase.Lobby, rig.Server.Phase);

            // A guest may not change the setup; the host may.
            guest.SetSetup(guest.Lobby.Setup with { TimeLimit = 7 });
            host.SetSetup(host.Lobby.Setup with { TimeLimit = 3, SpyLives = 2 });
            Assert.True(rig.RunUntil(() => guest.Lobby.Setup.TimeLimit == 3 && guest.Lobby.Setup.SpyLives == 2, 5), Describe(rig));
        }
    }

    [Fact]
    public void FullServerPasswordsAndBansAreRefusedWithReadableReasons()
    {
        var now = DateTimeOffset.Parse("2026-10-23T12:00:00Z", CultureInfo.InvariantCulture);
        var bans = new BanList(null, () => now);
        (OnlineRig rig, SimLinkNetwork network) = Create(OnlineRig.Settings(MapCatalog.TestBox) with
        {
            MaxPlayers = 1, MaxSpectators = 0, Password = "swordfish", Bans = bans, AdminPassword = "root",
        }, SimLinkProfile.A);
        using (rig)
        {
            OnlineClient nopass = Join(rig, network, "NoPass", 0);
            OnlineClient wrong = Join(rig, network, "Wrong", 0, s => s with { Password = "trout" });
            Assert.True(rig.RunUntil(() => nopass.State == OnlineClientState.Failed && wrong.State == OnlineClientState.Failed, 10));
            Assert.Equal("This server needs a password.", nopass.Error);
            Assert.Equal("Wrong password.", wrong.Error);

            OnlineClient first = Join(rig, network, "First", 0, s => s with { Password = "swordfish" });
            Assert.True(rig.RunUntil(() => first.State == OnlineClientState.Lobby, 10), Describe(rig));
            OnlineClient second = Join(rig, network, "Second", 0, s => s with { Password = "swordfish" });
            Assert.True(rig.RunUntil(() => second.State == OnlineClientState.Failed, 10));
            Assert.Equal("The server is full", second.Error);

            // The console bans the first player: it is told why, and cannot come back.
            Assert.Contains("banned First", rig.Server.Execute("ban First cheating"));
            Assert.True(rig.RunUntil(() => first.State == OnlineClientState.Failed, 10));
            Assert.Equal("cheating", first.Error);
            OnlineClient again = Join(rig, network, "First", 0, s => s with { Password = "swordfish" });
            Assert.True(rig.RunUntil(() => again.State == OnlineClientState.Failed, 10));
            Assert.Equal("You are banned from this server: cheating", again.Error);
            Assert.Single(bans.Entries);
        }
    }

    [Fact]
    public void AdminAnnouncementsAndPrivateRepliesReachPlayersDuringTheMatchOnce()
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        (OnlineRig rig, SimLinkNetwork network) = Create(OnlineRig.Settings(MapCatalog.TestBox) with { AdminPassword = "root" }, SimLinkProfile.B);
        using (rig)
        {
            OnlineClient admin = Join(rig, network, "Admin", 0);
            OnlineClient guest = Join(rig, network, "Guest", 1);
            Assert.True(rig.RunUntil(() => admin.State == OnlineClientState.Lobby && guest.State == OnlineClientState.Lobby &&
                admin.Lobby.Players.Count == 2 && admin.Lobby.Players.All(p => p.Map == MapState.Ok), 15), Describe(rig));
            admin.SetReady(true);
            guest.SetReady(true);
            Assert.True(rig.RunUntil(() => admin.State == OnlineClientState.Match && guest.State == OnlineClientState.Match, 30), Describe(rig));

            var adminChat = new List<ChatLine>();
            var guestChat = new List<ChatLine>();
            var adminNotices = new List<string>();
            var guestNotices = new List<string>();
            admin.Session!.ChatReceived += adminChat.Add;
            guest.Session!.ChatReceived += guestChat.Add;
            admin.Session.SessionLineReceived += line => CollectNotice(line, adminNotices);
            guest.Session.SessionLineReceived += line => CollectNotice(line, guestNotices);

            string announcement = new('A', 150);
            const string notice = "Server maintenance starts after this match.";
            rig.Server.Execute("say " + announcement);
            rig.Server.Announce(notice);
            admin.Admin("root");
            Assert.True(rig.RunUntil(() => adminChat.Any(c => c.Text == announcement) && guestChat.Any(c => c.Text == announcement) &&
                adminNotices.Contains(notice) && guestNotices.Contains(notice) &&
                adminNotices.Contains("You are an admin of this server."), 10), Describe(rig));
            admin.Command("status");
            Assert.True(rig.RunUntil(() => adminNotices.Any(n => n.StartsWith("Test server: match", StringComparison.Ordinal)), 10), Describe(rig));
            rig.RunUntil(() => false, 1);

            foreach (List<ChatLine> chat in new[] { adminChat, guestChat })
            {
                ChatLine line = Assert.Single(chat, c => c.Text == announcement);
                Assert.Equal(0, line.SenderId);
                Assert.Equal("Server", line.SenderName);
                Assert.Equal(ChatK.NoTeam, line.SenderTeam);
            }
            foreach (List<string> messages in new[] { adminNotices, guestNotices })
            {
                Assert.Single(messages, m => m == notice);
                Assert.DoesNotContain(messages, m => m.Contains(announcement, StringComparison.Ordinal));
            }
            foreach (OnlineClient client in new[] { admin, guest })
                Assert.Single(client.Lobby.Messages, m => m.Text.Contains(announcement, StringComparison.Ordinal));
            Assert.Single(adminNotices, m => m == "You are an admin of this server.");
            Assert.DoesNotContain("You are an admin of this server.", guestNotices);
            Assert.DoesNotContain(guestNotices, m => m.StartsWith("Test server: match", StringComparison.Ordinal));
        }
    }

    private static void CollectNotice(string line, List<string> into)
    {
        if (SessionProtocol.TryParse(line, out string command, out List<string> fields, fieldCount: 1) && command == "msg")
            into.Add(SessionProtocol.Text(fields, 0));
    }

    [Fact]
    public void ConsoleCommandsChangeTheSetupAndReportTheSession()
    {
        (OnlineRig rig, SimLinkNetwork network) = Create(OnlineRig.Settings(MapCatalog.TestBox), SimLinkProfile.A);
        using (rig)
        {
            OnlineClient a = Join(rig, network, "Alpha", 0);
            Assert.True(rig.RunUntil(() => a.State == OnlineClientState.Lobby, 10), Describe(rig));
            Assert.Contains(rig.Server.Execute("set time 5"), l => l.Contains("5 min", StringComparison.Ordinal));
            Assert.Contains(rig.Server.Execute("set revenge on"), l => l.Contains("revenge", StringComparison.Ordinal));
            Assert.True(rig.RunUntil(() => a.Lobby.Setup.TimeLimit == 5 && a.Lobby.Setup.Revenge && a.Lobby.Setup.GamesInARow == 2, 5));
            IReadOnlyList<string> status = rig.Server.Execute("status");
            Assert.StartsWith("Test server: lobby, TestBox Story, 5 min", status[0], StringComparison.Ordinal);
            Assert.Contains(status, l => l.Contains("Alpha", StringComparison.Ordinal));
            rig.Server.Execute("say hello everyone");
            Assert.True(rig.RunUntil(() => a.Lobby.Messages.Any(m => m.Sender == "Server" && m.Text == "hello everyone"), 5),
                string.Join(" | ", a.Lobby.Messages.Select(m => m.Sender + ":" + m.Text)));
            Assert.Contains("unknown map", rig.Server.Execute("map NoSuchMap")[0], StringComparison.Ordinal);
            rig.Server.Execute("close");
            Assert.True(rig.RunUntil(() => rig.Server.CloseRequested, 2));
            Assert.True(rig.RunUntil(() => a.State == OnlineClientState.Failed, 5));
            Assert.Equal("The server is shutting down.", a.Error);
        }
    }

    private static string Describe(OnlineRig rig) =>
        rig.Server.Status() + " | " + string.Join(" | ", rig.Clients.Select(c =>
            $"{c.State} {c.CurrentMap} {c.Error} players={c.Lobby.Players.Count} log={string.Join("/", c.ConnectLog)}"));
}
