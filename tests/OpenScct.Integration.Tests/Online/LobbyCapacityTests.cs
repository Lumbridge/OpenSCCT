using System;
using System.Linq;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class LobbyCapacityTests
{
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
