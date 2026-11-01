using System;
using System.Globalization;
using System.Linq;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Integration.Tests.Online;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests;

/// <summary>
/// The Create Session page's options and the lobby room's rules in the online session
/// (spec/game/lobby-and-match-creation.md): every option reaches the players and the server, the page's ranges hold,
/// a change un-readies the guests, Merc Deathmatch puts everyone on the mercenaries, Revenge plays the pair with the
/// sides swapped, and after the series everyone is back in the lobby (with a new map when Random Map is on).
/// </summary>
public sealed class LobbyOptionsTests
{
    private static bool TestBoxMissing => WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null;

    private static (OnlineRig Rig, SimLinkNetwork Network) Create(OnlineServerSettings settings)
    {
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 5, SimLinkProfile.A);
        var server = new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds);
        return (new OnlineRig(server, clock), network);
    }

    private static OnlineClient Join(OnlineRig rig, SimLinkNetwork network, string name, int team, string? hostKey = null)
    {
        int n = 0;
        var target = JoinTarget.Custom("Options server", () => network.Bind(name + "-" + (n++).ToString(CultureInfo.InvariantCulture)), "server");
        return rig.Add(new OnlineClient(target, OnlineRig.ClientSettings(name, team) with { HostKey = hostKey }, rig.Now));
    }

    private static OnlineServerSettings Listen(Func<LobbySetup, LobbySetup>? setup = null)
    {
        OnlineServerSettings s = OnlineRig.Settings(MapCatalog.TestBox, timeLimit: 1) with { Dedicated = false, HostKey = "k" };
        return s with { Setup = setup is null ? s.Setup : setup(s.Setup) };
    }

    private static string Describe(OnlineRig rig) =>
        rig.Server.Status() + " | " + string.Join(" | ", rig.Clients.Select(c =>
            $"{c.Settings.PlayerName}: {c.State} {c.CurrentMap} {c.Error} team={c.Lobby.Local?.Team} ready={c.Lobby.Local?.Ready}"));

    [Theory]
    [InlineData("kick")]
    [InlineData("ban")]
    [InlineData("voiceban")]
    [InlineData("chatvoiceban")]
    [InlineData("commandban")]
    public void OpenDedicatedPlayersCanConfigureButCannotModerate(string action)
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Dedicated = true, OpenMapCommands = true, AutoLaunch = false,
        };
        (OnlineRig rig, SimLinkNetwork network) = Create(settings);
        using (rig)
        {
            OnlineClient player = Join(rig, network, "Player", 0);
            OnlineClient other = Join(rig, network, "Other", 1);
            Assert.True(rig.RunUntil(() => player.State == OnlineClientState.Lobby &&
                other.State == OnlineClientState.Lobby && player.Lobby.Players.Count == 2, 10), Describe(rig));
            Assert.True(player.Lobby.CanControl);
            Assert.False(player.Lobby.Local!.CanModerate);
            player.SetSetup(player.Lobby.Setup with { RespawnSeconds = 3 });
            Assert.True(rig.RunUntil(() => other.Lobby.Setup.RespawnSeconds == 3, 5), Describe(rig));
            int id = other.Lobby.Local!.Id;
            switch (action)
            {
                case "kick": player.Kick(id); break;
                case "ban": player.Ban(id); break;
                case "voiceban": player.SetVoiceBanned(id, true); break;
                case "chatvoiceban": player.Chat("!voiceban Other"); break;
                case "commandban": player.Command("ban Other"); break;
            }
            rig.RunUntil(() => false, 2);
            Assert.Equal(OnlineClientState.Lobby, other.State);
            Assert.Equal(2, player.Lobby.Players.Count);
            Assert.False(other.Lobby.Local!.VoiceBanned);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostAndAuthenticatedAdminRetainModeration(bool dedicated)
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        var settings = Listen() with
        {
            Dedicated = dedicated, AdminPassword = "test-admin", AutoLaunch = false,
        };
        (OnlineRig rig, SimLinkNetwork network) = Create(settings);
        using (rig)
        {
            OnlineClient moderator = Join(rig, network, "Moderator", 0, dedicated ? null : "k");
            OnlineClient other = Join(rig, network, "Other", 1);
            Assert.True(rig.RunUntil(() => moderator.State == OnlineClientState.Lobby &&
                other.State == OnlineClientState.Lobby && moderator.Lobby.Players.Count == 2, 10), Describe(rig));
            if (dedicated) moderator.Admin("test-admin");
            Assert.True(rig.RunUntil(() => moderator.Lobby.Local!.CanModerate, 5), Describe(rig));
            moderator.SetVoiceBanned(other.Lobby.Local!.Id, true);
            Assert.True(rig.RunUntil(() => other.Lobby.Local!.VoiceBanned, 5), Describe(rig));
            moderator.Kick(other.Lobby.Local.Id);
            Assert.True(rig.RunUntil(() => other.State != OnlineClientState.Lobby, 5), Describe(rig));
        }
    }

    [Fact]
    public void EveryCreateSessionOptionRoundTripsWithinThePagesRanges()
    {
        var setup = new LobbySetup
        {
            Map = "SteeE", Mode = EGameMode.GM_DiskHunt, TimeLimit = 15, ScoreLimit = 5, SpyLives = 6, MercLives = 0, RespawnSeconds = 3, Disks = 8,
            DisksToWin = 5, DisksMax = 30, GamesInARow = 4, MaxPlayers = 6, VoiceMode = EVoiceChatMode.VCM_Classic, VoiceInLobby = false,
            RandomMap = true,
        };
        LobbySetup back = LobbySetup.FromOptions(setup.ToOptions());
        Assert.Equal(setup.Clamped(), back);
        Assert.Equal((EVoiceChatMode.VCM_Classic, false, true, 4, 3), (back.VoiceMode, back.VoiceInLobby, back.RandomMap, back.GamesInARow, back.RespawnSeconds));

        // The page's ranges (GUI.u Session* rows): respawn 1..15 once edited, games 1..10, an unknown voice index is Modern.
        LobbySetup wild = LobbySetup.FromOptions("?MapName=X?GameMode=5?TimesRespawnsATT=0?GamesInARow=11?SvmVoice=7?SvmVoiceLobby=0");
        Assert.Equal((1, 10, EVoiceChatMode.VCM_Modern, false), (wild.RespawnSeconds, wild.GamesInARow, wild.VoiceMode, wild.VoiceInLobby));
        Assert.Equal(15, LobbySetup.FromOptions("?MapName=X?GameMode=5?TimesRespawnsATT=40").RespawnSeconds);

        // Revenge forces two games; the voice row's names and hints are the page's, in its order.
        Assert.Equal(2, (setup with { Revenge = true }).Clamped().GamesInARow);
        Assert.Equal(["Modern", "Classic", "Team", "Open"], LobbySetup.VoiceModeNames);
        Assert.Equal("Living team - hackable coms", LobbySetup.VoiceModeHints[1]);
        Assert.Equal(EVoiceChatMode.VCM_Open, LobbySetup.VoiceModeOf(3));
        Assert.Equal((2, 8, 4, 16), (LobbySetup.MinMaxPlayers, LobbySetup.MaxMaxPlayers, LobbySetup.DefaultMaxPlayers, LobbySetup.MaxPasswordLength));

        // The page writes the voice values where the game reads them.
        var gri = new GameReplicationInfo();
        setup.ApplyTo(gri);
        Assert.Equal((EVoiceChatMode.VCM_Classic, false, true), (gri.VoiceChatMode, gri.allowVoiceChatInLobby, gri.bRandomMap));
        Assert.Contains("voice Classic", setup.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void AChangeUnreadiesTheGuestsAndReachesEveryLobby()
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        (OnlineRig rig, SimLinkNetwork network) = Create(Listen());
        using (rig)
        {
            OnlineClient host = Join(rig, network, "Host", 0, "k");
            OnlineClient guest = Join(rig, network, "Guest", 1);
            Assert.True(rig.RunUntil(() => host.State == OnlineClientState.Lobby && guest.State == OnlineClientState.Lobby &&
                host.Lobby.Players.Count == 2, 15), Describe(rig));
            guest.SetReady(true);
            Assert.True(rig.RunUntil(() => host.Lobby.Players.Any(p => p.Name == "Guest" && p.Ready), 5), Describe(rig));

            // The host changes only the voice mode and the respawn: the ready guest is un-readied (Lobby_Create rule 12).
            host.SetSetup(host.Lobby.Setup with { VoiceMode = EVoiceChatMode.VCM_Team, RespawnSeconds = 2, RandomMap = true });
            Assert.True(rig.RunUntil(() => guest.Lobby.Setup.VoiceMode == EVoiceChatMode.VCM_Team && guest.Lobby.Setup.RespawnSeconds == 2 &&
                guest.Lobby.Setup.RandomMap, 5), Describe(rig));
            Assert.True(rig.RunUntil(() => host.Lobby.Players.Any(p => p.Name == "Guest" && !p.Ready), 5), Describe(rig));
            Assert.Contains(guest.Lobby.Messages, m => m.Text.Contains("voice Team", StringComparison.Ordinal));

            // Lobby chat (the Lobby Room's Say and TeamSay) reaches the other player with its sender.
            guest.Chat("hello from the guest");
            host.Chat("hello team", teamOnly: true);
            Assert.True(rig.RunUntil(() => host.Lobby.Messages.Any(m => m.Sender == "Guest" && m.Text == "hello from the guest"), 5),
                string.Join(" | ", host.Lobby.Messages.Select(m => m.Sender + ":" + m.Text)));

            // A guest may not change it.
            guest.SetSetup(guest.Lobby.Setup with { VoiceMode = EVoiceChatMode.VCM_Open });
            rig.RunUntil(() => false, 1);
            Assert.Equal(EVoiceChatMode.VCM_Team, rig.Server.Setup.VoiceMode);
        }
    }

    [Fact]
    public void MercDeathmatchPutsEveryoneOnTheMercenaries()
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        (OnlineRig rig, SimLinkNetwork network) = Create(Listen());
        using (rig)
        {
            OnlineClient host = Join(rig, network, "Host", 0, "k");
            OnlineClient guest = Join(rig, network, "Guest", 0);
            Assert.True(rig.RunUntil(() => host.Lobby.Players.Count == 2 && guest.State == OnlineClientState.Lobby, 15), Describe(rig));
            host.SetSetup(host.Lobby.Setup with { Mode = EGameMode.GM_MercDeathMatch, ScoreLimit = 15 });
            Assert.True(rig.RunUntil(() => guest.Lobby.Players.All(p => p.Team == 1) && guest.Lobby.Setup.Mode == EGameMode.GM_MercDeathMatch, 5),
                Describe(rig));

            // Switch Team does nothing there; the server says why.
            guest.SetTeam(0);
            Assert.True(rig.RunUntil(() => guest.Lobby.Messages.Any(m => m.Text.Contains("Merc Deathmatch", StringComparison.Ordinal)), 5));
            Assert.Equal(1, guest.Lobby.Local!.Team);
        }
    }

    [Fact]
    public void RevengeSwapsTheSidesThenEveryoneIsBackInTheLobbyWithARandomMap()
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        (OnlineRig rig, SimLinkNetwork network) = Create(Listen(s => s with { Revenge = true, RandomMap = true }));
        using (rig)
        {
            OnlineClient host = Join(rig, network, "Host", 0, "k");
            OnlineClient guest = Join(rig, network, "Guest", 1);
            Assert.True(rig.RunUntil(() => host.Lobby.Players.Count == 2 && guest.State == OnlineClientState.Lobby, 15), Describe(rig));
            Assert.Equal(2, guest.Lobby.Setup.GamesInARow);
            guest.SetReady(true);
            Assert.True(rig.RunUntil(() => host.Lobby.Players.Any(p => p.Name == "Guest" && p.Ready), 5), Describe(rig));
            host.Launch();

            // Game one: the guest is a merc; the round runs out (mercs win on time); both go on.
            Assert.True(rig.RunUntil(() => guest.State == OnlineClientState.Match && guest.Session?.LocalPlayer?.PlayerReplicationInfo?.Team is not null, 30),
                Describe(rig));
            Assert.Equal(1, guest.Session!.LocalPlayer!.PlayerReplicationInfo!.Team!.TeamIndex);
            Assert.True(rig.RunUntil(() => guest.Results is not null && host.Results is not null, 90), Describe(rig));
            Assert.Equal(1, guest.Results!.GamesLeft);
            host.SetReady(true);
            guest.SetReady(true);

            // Game two of the pair: the sides are swapped (round-travel.md rule 10).
            Assert.True(rig.RunUntil(() => rig.Server.RoundsStarted == 2 && guest.State == OnlineClientState.Match &&
                guest.Session?.LocalPlayer?.PlayerReplicationInfo?.Team is not null, 40), Describe(rig));
            Assert.Equal(0, guest.Session!.LocalPlayer!.PlayerReplicationInfo!.Team!.TeamIndex);
            Assert.True(rig.RunUntil(() => guest.State == OnlineClientState.Results && guest.Results is { GamesLeft: 0 }, 90), Describe(rig));
            host.SetReady(true);
            guest.SetReady(true);

            // The pair is over: everyone is back in the lobby (not a rematch), on the sides they chose there, and Random
            // Map drew another map.
            Assert.True(rig.RunUntil(() => rig.Server.Phase == SessionPhase.Lobby && host.State == OnlineClientState.Lobby &&
                guest.State == OnlineClientState.Lobby, 30), Describe(rig));
            Assert.Equal(2, rig.Server.RoundsStarted);
            Assert.True(rig.RunUntil(() => guest.Lobby.Players.Any(p => p.Name == "Guest" && p.Team == 1) &&
                guest.Lobby.Players.Any(p => p.Name == "Host" && p.Team == 0), 5), Describe(rig));
            bool otherStoryMap = WalkerRig.Maps.ListMaps().Any(m => m.Id != MapCatalog.TestBox &&
                WalkerRig.Maps.GetMapSettings(m.Id) is { } ms && ms.SearchForGameMode(EGameMode.GM_Multi) >= 0);
            if (otherStoryMap)
            {
                Assert.True(rig.RunUntil(() => guest.Lobby.Setup.Map != MapCatalog.TestBox, 5), Describe(rig));
                Assert.Contains(guest.Lobby.Messages, m => m.Text.StartsWith("Next: ", StringComparison.Ordinal));
            }
        }
    }
}
