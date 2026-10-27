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

/// <summary>The online layer's parts: wire formats, the setup's options, the session transport, bans, a revenge series.</summary>
public sealed class OnlinePartsTests
{
    [Fact]
    public void ServerInfoAndDiscoveryPacketsRoundTrip()
    {
        var info = new ServerInfo
        {
            Name = "Squat night", Map = "SteeE", Mode = 5, Phase = SessionPhase.Match, Players = 3, MaxPlayers = 4, GamePort = 28888,
            ProtocolHash = 0x1122334455667788, ContentHash = 0x99aabbccddeeff00, HasPassword = true, Dedicated = true, SecondsLeft = 431,
            PlayerList = [new ServerInfoPlayer("Sam", 0), new ServerInfoPlayer("Shadownet", 1), new ServerInfoPlayer("Watcher", 255)],
        };
        var buffer = new byte[DiscoveryPackets.MaxBytes];
        int n = DiscoveryPackets.WriteInfo(buffer, 77, info);
        Assert.True(DiscoveryPackets.TryReadInfo(buffer.AsSpan(0, n), out ulong nonce, out ServerInfo back));
        Assert.Equal(77UL, nonce);
        Assert.Equal(info with { PlayerList = [] }, back with { PlayerList = [] });
        Assert.Equal(info.PlayerList, back.PlayerList);

        // Queries are padded; a short one is not answered (no amplification).
        int q = DiscoveryPackets.WriteQuery(buffer, 5);
        Assert.True(DiscoveryPackets.IsQuery(buffer.AsSpan(0, q)));
        Assert.False(DiscoveryPackets.IsQuery(buffer.AsSpan(0, DiscoveryPackets.HeaderBytes)));
        var limiter = new AnswerLimiter();
        int allowed = Enumerable.Range(0, 20).Count(_ => limiter.Allow(System.Net.IPAddress.Loopback, 1000));
        Assert.Equal(DiscoveryPackets.AnswersPerSecond, allowed);
        Assert.True(limiter.Allow(System.Net.IPAddress.Loopback, 2000));
    }

    [Fact]
    public void RelayFramesCarryTokensAndSessions()
    {
        var frame = new byte[RelayFrames.MaxFrameBytes];
        int n = RelayFrames.WriteTokenHeader(frame, RelayFrames.ClientData, 0xdeadbeefcafef00d);
        Assert.Equal(RelayFrames.ClientHeaderBytes, n);
        Assert.True(RelayFrames.IsRelayFrame(frame));
        Assert.True(RelayFrames.TryReadToken(frame, out ulong token));
        Assert.Equal(0xdeadbeefcafef00dUL, token);
        n = RelayFrames.WriteSessionHeader(frame, RelayFrames.HostData, 0x01020304);
        Assert.True(RelayFrames.TryReadSession(frame.AsSpan(0, n), out uint session));
        Assert.Equal(0x01020304u, session);
        Assert.True(RelayFrames.TryReadError(RelayFrames.ErrorFrame(RelayFrames.ErrorCode.RateLimited, "slow down"), out var code, out string text));
        Assert.Equal((RelayFrames.ErrorCode.RateLimited, "slow down"), (code, text));
        Assert.Equal("deadbeefcafef00d", RelayFrames.TokenText(0xdeadbeefcafef00d));
        Assert.True(RelayFrames.TryParseToken("deadbeefcafef00d", out ulong parsed) && parsed == 0xdeadbeefcafef00d);
        Assert.False(RelayFrames.TryParseToken("0", out _));
        Assert.Equal("relay://example.org:28881/00000000000000ff", LiteNetLibTransport.RelayAddress("example.org:28881", 255));
    }

    [Fact]
    public void LobbySetupStartsFromTheMapAndRoundTripsItsOptions()
    {
        // SteeE's Story entry (spec/game/match-options.md): 10 min, 4 spy lives, 3 merc lives, 10 s respawn.
        if (WalkerRig.Maps.GetMapSettings("SteeE") is { } steee)
        {
            LobbySetup story = LobbySetup.ForMap("SteeE", EGameMode.GM_Multi, steee);
            Assert.Equal((10, 4, 3, 10), (story.TimeLimit, story.SpyLives, story.MercLives, story.RespawnSeconds));
            Assert.Contains("?GameMode=5?LivesATT=4?TimesRespawnsATT=10?LivesDEF=3", story.ToGameOptions(4), StringComparison.Ordinal);
            Assert.EndsWith("?NBPlayers=4", story.ToGameOptions(4), StringComparison.Ordinal);
        }

        var setup = new LobbySetup
        {
            Map = "SteeE", Mode = EGameMode.GM_DiskHunt, TimeLimit = 12, ScoreLimit = 3, SpyLives = 3, MercLives = 2, RespawnSeconds = 8, Disks = 5,
            DisksToWin = 3, DisksMax = 30, Revenge = true, MaxPlayers = 6,
        };
        LobbySetup back = LobbySetup.FromOptions(setup.ToOptions());
        Assert.Equal(setup.Clamped(), back);
        Assert.Equal(2, back.GamesInARow);

        // The menu's ranges hold whatever a client sends.
        LobbySetup wild = LobbySetup.FromOptions("?MapName=X?GameMode=5?TimeLimit=99?LivesATT=50?LivesDEF=-3?TimesRespawnsATT=0");
        Assert.Equal((20, 10, 0, LobbySetup.MinRespawn), (wild.TimeLimit, wild.SpyLives, wild.MercLives, wild.RespawnSeconds));
        Assert.True(LobbySetup.TryParseMode("Merc Deathmatch", out EGameMode mdm) && mdm == EGameMode.GM_MercDeathMatch);
    }

    [Fact]
    public void TheEngineKeepsTheLastValueOfARepeatedOption()
    {
        Assert.Equal("?MapName=SteeE?GamesInARow=1?Revenge=True", NativeUrl.Rebuild("?MapName=SteeE?GamesInARow=2?Revenge=True?GamesInARow=1"));
        Assert.Equal("?MapName=SteeE?gamesinarow=1", NativeUrl.Rebuild("?MapName=SteeE?GamesInARow=2?gamesinarow=1"));
        Assert.Equal("?A=1?B", NativeUrl.Rebuild("?A=0?B?A=1"));
        Assert.Equal(string.Empty, NativeUrl.Rebuild(string.Empty));
    }

    [Fact]
    public void SessionLinesSplitIntoFields()
    {
        string line = SessionProtocol.Line("player", 3, 1, 5, 42, 1, "Sam Fisher");
        Assert.True(SessionProtocol.TryParse(line, out string command, out List<string> f, 6));
        Assert.Equal("player", command);
        Assert.Equal(["3", "1", "5", "42", "1", "Sam Fisher"], f);
        Assert.False(SessionProtocol.TryParse("hello there", out _, out _));
        Assert.Equal("a b", SessionProtocol.Clean("a\nb"));
    }

    [Fact]
    public void BansMatchAnyIdentityAndExpire()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-23T12:00:00Z", CultureInfo.InvariantCulture);
        var bans = new BanList(null, () => now);
        bans.Add("install-1", "203.0.113.5:50000", "Griefer", "spam", 10);
        Assert.NotNull(bans.Find("install-1", null, null));
        Assert.NotNull(bans.Find(null, "relay 203.0.113.5:40000", null));
        Assert.NotNull(bans.Find(null, null, "griefer"));
        Assert.Null(bans.Find("install-2", "198.51.100.1:1", "Friend"));
        now = now.AddMinutes(11);
        Assert.Null(bans.Find("install-1", null, null));
        Assert.Equal("::1", BanList.IpOf("[::1]:28888"));
    }

    [Fact]
    public void ARetiredGenerationNeitherSendsNorReceivesAndHoldsItsPeers()
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 3, SimLinkProfile.A);
        using SimLinkTransport a = network.Bind("a");
        using SimLinkTransport b = network.Bind("b");
        using var session = new SessionTransport(a, ownsInner: false);
        SessionTransport.Generation first = session.NewGeneration(holdDisconnects: true);
        EndpointId toB = first.Connect("b");
        Assert.True(first.Send(toB, [1, 2, 3]));
        first.Disconnect(toB);
        SessionTransport.Generation second = session.NewGeneration();
        Assert.True(first.Retired);
        Assert.False(first.Send(toB, [4]));

        // b answers; only the current generation sees it.
        clock.Advance(1000);
        var buffer = new byte[1500];
        Assert.True(b.TryReceive(buffer, out EndpointId toA, out _));
        Assert.True(b.Send(toA, [9]));
        clock.Advance(1000);
        Assert.False(first.TryReceive(buffer, out _, out _));
        Assert.True(second.TryReceive(buffer, out EndpointId from, out int length));
        Assert.Equal((toB, 1, 9), (from, length, (int)buffer[0]));
        Assert.Equal(toB, second.Connect("b"));
    }

    [Fact]
    public void ARevengeSeriesSwapsSidesForItsSecondGameThenGoesBackToTheLobby()
    {
        Assert.SkipWhen(WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null, "The TestBox fixture is not in this checkout.");
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 11, SimLinkProfile.A);
        OnlineServerSettings settings = OnlineRig.Settings(MapCatalog.TestBox, timeLimit: 1);
        settings = settings with { Setup = settings.Setup with { Revenge = true } };
        using var rig = new OnlineRig(new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds), clock);
        OnlineClient Join(string name, int team)
        {
            int n = 0;
            return rig.Add(new OnlineClient(JoinTarget.Custom("server", () => network.Bind(name + (n++).ToString(CultureInfo.InvariantCulture)), "server"),
                OnlineRig.ClientSettings(name, team), clock.NowMicroseconds));
        }

        OnlineClient sam = Join("Sam", 0);
        OnlineClient merc = Join("Merc", 1);
        Assert.True(rig.RunUntil(() => sam.Lobby.Players.Count == 2 && merc.Lobby.Players.Count == 2, 10));
        sam.SetReady(true);
        merc.SetReady(true);

        int TeamOf(string name) => rig.Server.Players.Single(p => p.Name == name).Slot!.Controller!.PlayerReplicationInfo!.Team!.TeamIndex;
        Assert.True(rig.RunUntil(() => rig.Server.Phase == SessionPhase.Match, 20));
        Assert.Equal((0, 1), (TeamOf("Sam"), TeamOf("Merc")));
        Assert.True(rig.RunUntil(() => sam.Results is not null, 90));
        Assert.Equal(1, sam.Results!.GamesLeft);

        // The second game of the pair: Login swaps each player's side (round-travel.md rule 10).
        sam.SetReady(true);
        merc.SetReady(true);
        Assert.True(rig.RunUntil(() => rig.Server.Phase == SessionPhase.Match && rig.Server.RoundsStarted == 2 &&
            rig.Server.Players.All(p => p.Slot?.Controller?.PlayerReplicationInfo?.Team is not null), 30));
        Assert.Equal((1, 0), (TeamOf("Sam"), TeamOf("Merc")));
        MatchResults? first = sam.Results;
        Assert.True(rig.RunUntil(() => sam.Results is not null && !ReferenceEquals(sam.Results, first), 90));
        Assert.Equal(0, sam.Results!.GamesLeft);

        // The series is over: the dedicated server's lobby is next.
        sam.SetReady(true);
        merc.SetReady(true);
        Assert.True(rig.RunUntil(() => rig.Server.Phase == SessionPhase.Lobby && sam.State == OnlineClientState.Lobby, 30));
    }
}
