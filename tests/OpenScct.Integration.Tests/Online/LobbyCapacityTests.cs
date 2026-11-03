using System;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenScct.Core;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class LobbyCapacityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ListenHostCanRejoinWhenSpectatorPlacesAreFull(bool recording)
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is not in this checkout.");
        string demoPath = Path.Combine(Path.GetTempPath(), "openscct-host-capacity-" + Guid.NewGuid().ToString("N") + ".scctdemo");
        try
        {
            var settings = OnlineRig.Settings(MapCatalog.TestBox) with
            {
                Dedicated = false, HostKey = "k", MaxPlayers = 3, MaxSpectators = 1, AutoLaunch = false,
            };
            var clock = new VirtualClock();
            var network = new SimLinkNetwork(clock, 25, SimLinkProfile.A);
            using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds), clock);
            int connection = 0;
            OnlineClient Join(string name, int team, string? key = null) => rig.Add(new OnlineClient(
                JoinTarget.Custom("Capacity server", () => network.Bind(name + "-" + (connection++).ToString(CultureInfo.InvariantCulture)), "server"),
                OnlineRig.ClientSettings(name, team) with { HostKey = key }, rig.Now));
            string Describe() => rig.Server.Status() + " | " + string.Join(" | ", rig.Clients.Select(c => $"{c.Settings.PlayerName}: {c.State} {c.Error}"));
            OnlineClient host = Join("Host", 0, "k");
            OnlineClient spy = Join("Spy", 0);
            OnlineClient merc = Join("Merc", 1);
            OnlineClient watcher = Join("Watcher", SessionProtocol.SpectatorTeam);
            Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Lobby && c.Lobby.Players.Count == 4), 10), Describe());
            spy.SetReady(true);
            merc.SetReady(true);
            Assert.True(rig.RunUntil(() => host.Lobby.Players.Where(p => !p.IsHost && !p.IsSpectator).All(p => p.Ready), 10), Describe());
            host.Launch();
            Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Match), 20), Describe());
            if (recording) rig.Server.Host.StartRecording(new DemoWriter(demoPath, new DemoHeader()), rig.Now);
            Assert.Equal(recording ? 3 : 2, rig.Server.Host.Game!.MaxSpectators);

            host.Leave();
            Assert.True(rig.RunUntil(() => rig.Server.Players.All(p => !p.IsHost), 10), Describe());
            Assert.Equal(SessionPhase.Match, rig.Server.Phase);
            OnlineClient returningHost = Join("Host", 0, "k");
            Assert.True(rig.RunUntil(() => returningHost.State == OnlineClientState.Match &&
                returningHost.Session?.LocalPlayer?.PlayerReplicationInfo?.bOnlySpectator == true, 10), Describe());
            Assert.True(returningHost.Lobby.IsHost);
            Assert.Equal(2, rig.Server.Players.Count(p => p.IsSpectator));
            Assert.Equal(recording ? 3 : 2, rig.Server.Host.Game.NumSpectators);

            OnlineClient overflow = Join("Overflow", SessionProtocol.SpectatorTeam);
            Assert.True(rig.RunUntil(() => overflow.State == OnlineClientState.Failed, 10), Describe());
            Assert.Equal("A round is being played and the spectator places are taken.", overflow.Error);
            if (recording)
            {
                rig.Server.Host.StopRecording();
                Assert.Equal(2, rig.Server.Host.Game.MaxSpectators);
                Assert.Equal(2, rig.Server.Host.Game.NumSpectators);
            }
        }
        finally { File.Delete(demoPath); }
    }

    [Fact]
    public void OnlineDefaultsAndProtocolSupportTwentyFourPlayerSlots()
    {
        Assert.Equal(24, new OnlineServerSettings().MaxPlayers);
        Assert.Equal(24, new LobbySetup().MaxPlayers);
        Assert.Equal(24, LobbySetup.ForMap(MapCatalog.TestBox, EGameMode.GM_Multi, null).MaxPlayers);
        LobbySetup setup = new LobbySetup { MaxPlayers = 24, AutoBalance = true }.Clamped();
        Assert.Equal(setup, LobbySetup.FromOptions(setup.ToOptions()));
        Assert.Equal(24, (setup with { MaxPlayers = 25 }).Clamped().MaxPlayers);
    }

    [Fact]
    public void ExpandedLobbyAdmitsTwelvePerTeamAndASpectatorThenLaunchesAllTwentyFourPlayers()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is not in this checkout.");
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            MaxPlayers = 2, MaxSpectators = 1, AutoLaunch = false, Profile = NetProfile.Enhanced,
        };
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 24, SimLinkProfile.A);
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds), clock);
        int connection = 0;
        OnlineClient Join(string name, int team) => rig.Add(new OnlineClient(
            JoinTarget.Custom("Capacity server", () => network.Bind(name + "-" + (connection++).ToString(CultureInfo.InvariantCulture)), "server"),
            OnlineRig.ClientSettings(name, team), rig.Now));
        string Describe() => rig.Server.Status() + $" | slots={rig.Server.Host.Players.Count} ready={rig.Server.Host.Game?.GameReplicationInfo?.PRIArray.Count(p => p.bWaitingPlayer && !p.bOnlySpectator)}" +
            " | " + string.Join(" | ", rig.Clients.Select(c =>
                $"{c.Settings.PlayerName}: {c.State} {c.Error} session={c.Session?.State} local={c.Session?.LocalPlayer is not null} waiting={c.Session?.ReportedWaiting}"));

        OnlineClient first = Join("Player0", 0);
        Assert.True(rig.RunUntil(() => first.State == OnlineClientState.Lobby, 10), Describe());
        first.SetSetup(first.Lobby.Setup with { MaxPlayers = 24, AutoBalance = true });
        Assert.True(rig.RunUntil(() => first.Lobby.MaxPlayers == 24, 10), Describe());
        OnlineClient[] players = new OnlineClient[24];
        players[0] = first;
        for (int i = 1; i < players.Length; i++) players[i] = Join("Player" + i.ToString(CultureInfo.InvariantCulture), 0);
        OnlineClient watcher = Join("Watcher", SessionProtocol.SpectatorTeam);
        Assert.True(rig.RunUntil(() => rig.Clients.All(c => c.State == OnlineClientState.Lobby && c.Lobby.Players.Count == 25), 15), Describe());
        Assert.All(rig.Clients, c =>
        {
            Assert.Equal(12, c.Lobby.Players.Count(p => p.Team == 0));
            Assert.Equal(12, c.Lobby.Players.Count(p => p.Team == 1));
            Assert.Equal(24, c.Lobby.MaxPlayers);
        });
        Assert.Equal((24, 24), (rig.Server.Info().Players, rig.Server.Info().MaxPlayers));
        Assert.Single(rig.Server.Players, p => p.IsSpectator);

        OnlineClient overflow = Join("Overflow", 1);
        Assert.True(rig.RunUntil(() => overflow.State == OnlineClientState.Failed, 10), Describe());
        Assert.Equal("The server is full", overflow.Error);
        watcher.SetTeam(1);
        rig.RunUntil(() => false, 1);
        Assert.Equal(SessionProtocol.SpectatorTeam, watcher.Lobby.Local!.Team);

        foreach (OnlineClient player in players) player.SetReady(true);
        Assert.True(rig.RunUntil(() => first.Lobby.Players.Where(p => !p.IsSpectator).All(p => p.Ready && p.Map == MapState.Ok), 10), Describe());
        first.Launch();
        Assert.True(rig.RunUntil(() => watcher.State == OnlineClientState.Match &&
            players.All(c => c.State == OnlineClientState.Match && c.Session?.LocalPlayer?.Pawn is not null), 30), Describe());
        Assert.Equal(24, rig.Server.Host.Game!.MaxPlayers);
        Assert.Equal(1, rig.Server.Host.Game.MaxSpectators);
        Assert.True(watcher.Session!.LocalPlayer!.PlayerReplicationInfo!.bOnlySpectator);
        Assert.Equal(25, rig.Server.Host.Players.Count);
        Assert.Equal(12, rig.Server.Host.Players.Count(p => p.Controller?.PlayerReplicationInfo?.Team?.TeamIndex == 0));
        Assert.Equal(12, rig.Server.Host.Players.Count(p => p.Controller?.PlayerReplicationInfo?.Team?.TeamIndex == 1));
    }

    [Fact]
    public void LobbyCapacityUpdatesListingsAndAdmissionWithoutEvictingHumansOrCountingSpectators()
    {
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Dedicated = false, HostKey = "k", MaxPlayers = 2, MaxSpectators = 1,
        };
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 23, SimLinkProfile.A);
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds), clock);
        OnlineClient Join(string name, int team, string? key = null) => rig.Add(new OnlineClient(
            JoinTarget.Custom("Capacity server", () => network.Bind(name), "server"),
            OnlineRig.ClientSettings(name, team) with { HostKey = key }, rig.Now));
        OnlineClient host = Join("Host", 0, "k");
        OnlineClient guest = Join("Guest", 1);
        OnlineClient watcher = Join("Watcher", SessionProtocol.SpectatorTeam);
        Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Lobby && guest.State == OnlineClientState.Lobby &&
            watcher.State == OnlineClientState.Lobby && host.Lobby.Players.Count == 3, 10));
        Assert.Equal(2, host.Lobby.MaxPlayers);
        Assert.Equal(2, rig.Server.Info().MaxPlayers);

        guest.SetSetup(guest.Lobby.Setup with { MaxPlayers = 8 });
        rig.RunUntil(() => false, 1);
        Assert.Equal(2, rig.Server.Setup.MaxPlayers); // A regular listen-server guest cannot edit capacity.
        watcher.SetTeam(0);
        rig.RunUntil(() => false, 1);
        Assert.Equal(SessionProtocol.SpectatorTeam, watcher.Lobby.Local!.Team);

        host.SetSetup(host.Lobby.Setup with { MaxPlayers = 4 });
        Assert.True(rig.RunUntil(() => guest.Lobby.MaxPlayers == 4 && watcher.Lobby.MaxPlayers == 4, 10));
        OnlineClient third = Join("Third", 0);
        Assert.True(rig.RunUntil(() => third.State == OnlineClientState.Lobby && host.Lobby.Players.Count == 4, 10));
        Assert.Equal(0, third.Lobby.Local!.Team);
        Assert.Equal(4, third.Lobby.MaxPlayers);

        host.SetSetup(host.Lobby.Setup with { MaxPlayers = 2 });
        Assert.True(rig.RunUntil(() => host.Lobby.MaxPlayers == 3 && guest.Lobby.MaxPlayers == 3 && watcher.Lobby.MaxPlayers == 3, 10));
        Assert.Equal(3, rig.Server.Info().MaxPlayers);
        Assert.Equal(4, rig.Server.Players.Count);
        Assert.All(rig.Clients, c => Assert.Equal(OnlineClientState.Lobby, c.State));
        OnlineClient full = Join("Full", 1);
        Assert.True(rig.RunUntil(() => full.State == OnlineClientState.Failed, 10));
        Assert.Equal("The server is full", full.Error);

        host.SetSetup(host.Lobby.Setup with { MaxPlayers = 4 });
        Assert.True(rig.RunUntil(() => watcher.Lobby.MaxPlayers == 4, 10));
        watcher.SetTeam(1);
        Assert.True(rig.RunUntil(() => watcher.Lobby.Local?.Team == 1, 10));
        Assert.Equal(4, rig.Server.Info().Players);
        Assert.Equal(4, rig.Server.Info().MaxPlayers);
    }
}
