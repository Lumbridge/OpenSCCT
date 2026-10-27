using System;
using System.Globalization;
using System.Linq;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Net.Voice;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

/// <summary>
/// Server bots in an online session (docs/ONLINE.md, "Bots"): the host's Create Session "Bots" fill the empty places of
/// a 2v2 as lobby rows, play the rounds on the server with the game's AI, are seen by a connected client as players,
/// never count as people for voice, keep their places across the revenge pair (swapped in the second game as everyone
/// is) and come back to the lobby, and make room or fill in as people come and go.
/// </summary>
public sealed class OnlineBotsTests
{
    private static bool TestBoxMissing => WalkerRig.Maps.FindFolder(MapCatalog.TestBox) is null;

    [Fact]
    public void TheRosterFillsEmptyPlacesAndNeverTakesAPersonsPlace()
    {
        var fill = new LobbySetup { Bots = BotFill.Fill };
        Assert.Equal((1, 1), OnlineBotRoster.Wanted(fill, 1, 1, 4));
        Assert.Equal((1, 2), OnlineBotRoster.Wanted(fill, 1, 0, 4));
        Assert.Equal((0, 2), OnlineBotRoster.Wanted(fill, 2, 0, 4));
        Assert.Equal((0, 0), OnlineBotRoster.Wanted(fill, 2, 2, 4));
        Assert.Equal((0, 1), OnlineBotRoster.Wanted(fill, 3, 0, 4));
        Assert.Equal((0, 3), OnlineBotRoster.Wanted(fill with { Mode = EGameMode.GM_MercDeathMatch }, 0, 1, 4));

        var perSide = new LobbySetup { Bots = BotFill.PerSide, BotsPerSide = 2 };
        Assert.Equal((2, 2), OnlineBotRoster.Wanted(perSide, 0, 0, 8));
        Assert.Equal((1, 1), OnlineBotRoster.Wanted(perSide, 1, 1, 4));
        Assert.Equal((0, 0), OnlineBotRoster.Wanted(new LobbySetup(), 0, 0, 8));

        // The setup carries them through the lobby protocol, within the page's ranges.
        LobbySetup setup = new LobbySetup { Bots = BotFill.PerSide, BotsPerSide = 3, BotSkill = AiSkill.Hard }.Clamped();
        Assert.Equal(setup, LobbySetup.FromOptions(setup.ToOptions()));
        Assert.Equal((BotFill.Off, 4, AiSkill.Expert),
            (LobbySetup.FromOptions("?MapName=X?GameMode=5?SvmBots=9?SvmBotsPerSide=40?SvmBotSkill=9") is var w ? (w.Bots, w.BotsPerSide, w.BotSkill) : default));
        Assert.Contains("Hard bots per side", setup.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void BotsFillA2v2PlayTheRevengePairWithAClientAndSurviveTheMapChanges()
    {
        Assert.SkipWhen(TestBoxMissing, "The TestBox fixture is not in this checkout.");
        OnlineServerSettings settings = OnlineRig.Settings(MapCatalog.TestBox, timeLimit: 1) with
        {
            Dedicated = false, HostKey = "k", Bots = OnlineServerBots.Factory(new AiNavCache(string.Empty)),
        };
        settings = settings with { Setup = settings.Setup with { Revenge = true, Bots = BotFill.Fill, BotSkill = AiSkill.Easy } };
        var clock = new VirtualClock();
        var network = new SimLinkNetwork(clock, 5, SimLinkProfile.A);
        var server = new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds);
        using var rig = new OnlineRig(server, clock);
        int n = 0;
        OnlineClient Join(string name, int team, string? key = null)
        {
            var target = JoinTarget.Custom("Bots server", () => network.Bind(name + "-" + (n++).ToString(CultureInfo.InvariantCulture)), "server");
            return rig.Add(new OnlineClient(target, OnlineRig.ClientSettings(name, team) with { HostKey = key }, rig.Now));
        }

        string Describe() =>
            server.Status() + " | " + string.Join(" | ", rig.Clients.Select(c =>
                $"{c.Settings.PlayerName}: {c.State} {c.CurrentMap} {c.Error} players={string.Join(",", c.Lobby.Players.Select(p => p.Name + "/" + p.Team + (p.IsBot ? "b" : "")))}"));

        OnlineClient host = Join("Host", 0, "k");
        OnlineClient guest = Join("Guest", 1);

        // The lobby: one bot on each side fills the 2v2; the client sees them as rows, ready, with the map.
        Assert.True(rig.RunUntil(() => guest.State == OnlineClientState.Lobby && guest.Lobby.Players.Count == 4 &&
            guest.Lobby.Players.Count(p => p.IsBot) == 2, 15), Describe());
        Assert.Equal(2, guest.Lobby.HumanCount);
        Assert.Contains(guest.Lobby.Players, p => p is { IsBot: true, Team: 0, Ready: true, Map: MapState.Ok });
        Assert.Contains(guest.Lobby.Players, p => p is { IsBot: true, Team: 1, Ready: true });
        string spyBot = guest.Lobby.Players.Single(p => p.IsBot && p.Team == 0).Name;
        string mercBot = guest.Lobby.Players.Single(p => p.IsBot && p.Team == 1).Name;

        // Voice is for people only: the lobby routes the two players, never a bot; the browser counts two players.
        Assert.Equal(2, server.LobbyVoiceMembers().Count);
        Assert.DoesNotContain(server.LobbyVoiceMembers(), id => server.Players.Single(p => p.Id == id).IsBot);
        Assert.Equal(2, server.Info().Players);

        guest.SetReady(true);
        Assert.True(rig.RunUntil(() => host.Lobby.Players.Any(p => p.Name == "Guest" && p.Ready), 5), Describe());
        host.Launch();

        // Game one: the bots log in on the server and the client sees four players.
        Assert.True(rig.RunUntil(() => guest.State == OnlineClientState.Match && guest.Session?.LocalPlayer?.PlayerReplicationInfo?.Team is not null &&
            guest.Session.World.Level.AllActors<PlayerReplicationInfo>().Count() >= 4, 40), Describe());
        Assert.NotNull(server.RoundBots);
        PlayerController spy = BotController(server, spyBot);
        PlayerController merc = BotController(server, mercBot);
        Assert.Equal((0, 1), (spy.PlayerReplicationInfo!.Team!.TeamIndex, merc.PlayerReplicationInfo!.Team!.TeamIndex));
        Assert.True(rig.RunUntil(() => spy.Pawn is not null && merc.Pawn is not null, 15), "the bots have pawns: " + Describe());

        // The AI plays: the spy bot walks off, and the client sees its pawn where the server has it.
        FVector start = spy.Pawn!.Location;
        Assert.True(rig.RunUntil(() => spy.Pawn is { } pawn && (pawn.Location - start).Size() > 150f, 20), "the spy bot walks: " + Describe());
        int spyPri = spy.PlayerReplicationInfo!.PlayerID;
        bool ClientSeesSpyBot()
        {
            foreach (Pawn seen in guest.Session!.World.Level.AllActors<Pawn>())
            {
                if (seen.PlayerReplicationInfo?.PlayerID == spyPri && spy.Pawn is { } real && (seen.Location - real.Location).Size() < 200f)
                {
                    return true;
                }
            }

            return false;
        }

        Assert.True(rig.RunUntil(ClientSeesSpyBot, 5), "the client sees the spy bot's pawn: " + Describe());
        var roster = new VoiceRoster();
        new NetDriverVoiceDirectory(server.Host.Driver).Fill(roster);
        Assert.Equal(2, roster.Count);
        Assert.True(rig.RunUntil(() => guest.Results is not null && host.Results is not null, 90), Describe());
        Assert.Equal(4, guest.Results!.Rows.Count);
        Assert.Contains(guest.Results.Rows, r => r.Name == spyBot);
        host.SetReady(true);
        guest.SetReady(true);

        // Game two of the pair: the bots are swapped with everyone.
        Assert.True(rig.RunUntil(() => server.RoundsStarted == 2 && guest.State == OnlineClientState.Match &&
            guest.Session?.LocalPlayer?.PlayerReplicationInfo?.Team is not null, 40), Describe());
        Assert.Equal(0, guest.Session!.LocalPlayer!.PlayerReplicationInfo!.Team!.TeamIndex);
        Assert.True(rig.RunUntil(() => server.RoundBots?.ControllerOf(IdOf(server, spyBot))?.PlayerReplicationInfo?.Team is not null, 15), Describe());
        Assert.Equal(1, BotController(server, spyBot).PlayerReplicationInfo!.Team!.TeamIndex);
        Assert.Equal(0, BotController(server, mercBot).PlayerReplicationInfo!.Team!.TeamIndex);
        Assert.True(rig.RunUntil(() => guest.State == OnlineClientState.Results && guest.Results is { GamesLeft: 0 }, 90), Describe());
        host.SetReady(true);
        guest.SetReady(true);

        // Back in the lobby: the same bots, on the sides they had there.
        Assert.True(rig.RunUntil(() => server.Phase == SessionPhase.Lobby && guest.State == OnlineClientState.Lobby &&
            guest.Lobby.Players.Count == 4, 30), Describe());
        Assert.Null(server.RoundBots);
        Assert.Contains(guest.Lobby.Players, p => p.Name == spyBot && p.IsBot && p.Team == 0);
        Assert.Contains(guest.Lobby.Players, p => p.Name == mercBot && p.IsBot && p.Team == 1);

        // The guest leaves: a bot takes his place among the mercenaries.
        guest.Leave();
        Assert.True(rig.RunUntil(() => host.Lobby.Players.Count(p => p.IsBot && p.Team == 1) == 2 && host.Lobby.Players.All(p => p.Name != "Guest"), 15),
            Describe());
        Assert.Single(host.Lobby.Players, p => p.IsBot && p.Team == 0);

        // A bot cannot be kicked; Bots off empties the rows.
        host.Kick(IdOf(server, spyBot));
        rig.RunUntil(() => false, 1);
        Assert.Contains(host.Lobby.Players, p => p.Name == spyBot);
        host.SetSetup(host.Lobby.Setup with { Bots = BotFill.Off });
        Assert.True(rig.RunUntil(() => host.Lobby.Players.Count == 1, 5), Describe());
    }

    private static int IdOf(OnlineServer server, string name) => server.Players.Single(p => p.Name == name).Id;

    private static PlayerController BotController(OnlineServer server, string name) =>
        server.RoundBots!.ControllerOf(IdOf(server, name)) ?? throw new InvalidOperationException($"{name} is not logged in");
}
