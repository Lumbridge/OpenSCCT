using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Net;
using OpenScct.Net.Discovery;
using OpenScct.Server;
using OpenScct.Server.Online;
using Xunit;

namespace OpenScct.Integration.Tests.Online;

public sealed class LobbyMapShortcutTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "svm-lobby-map-command-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(null, "!aQuAe")]
    [InlineData("root", "!aQuAe")]
    [InlineData(null, "!MaP aQuAe")]
    [InlineData("root", "!MaP aQuAe")]
    public void OrdinaryDedicatedPlayerSelectsCanonicalMapAndDefaultsWithoutLaunching(string? adminPassword, string command)
    {
        MapCatalog maps = Maps();
        LobbySetup original = new()
        {
            Map = MapCatalog.TestBox, Mode = EGameMode.GM_DiskHunt, TimeLimit = 19, SpyLives = 9, MercLives = 9,
            MaxPlayers = 6, AutoBalance = true,
            Alternate = AlternateRules.Default with { On = true, Abilities = AlternateRules.DefaultAbilities & ~AlternateAbility.SpyGrab },
            VoiceMode = EVoiceChatMode.VCM_Open, VoiceInLobby = false, RandomMap = true, Revenge = true,
            Practice = true, Sidearms = true, BotsPerSide = 3, SpyBots = 2, MercBots = 3,
            BotSkill = OpenScct.Game.AI.AiSkill.Hard,
        };
        using OnlineRig rig = Create(maps, out SimLinkNetwork network, original, adminPassword: adminPassword, autoLaunch: true);
        OnlineClient sender = Join(rig, network, maps, "Player", 0);
        OnlineClient observer = Join(rig, network, new MapCatalog(maps.Roots.Skip(1).ToArray()), "Observer", 1);
        WaitForLobby(rig, sender, observer);
        sender.SetReady(true);
        observer.SetReady(true);
        Assert.True(rig.RunUntil(() => sender.Lobby.Countdown > 0 && rig.Server.Players.All(p => p.Ready), 5), rig.Server.Status());
        ServerHost lobbyHost = rig.Server.Host;

        sender.Chat(command);
        Assert.True(rig.RunUntil(() => sender.Lobby.Setup.Map == "AquaE" && observer.Lobby.Setup.Map == "AquaE" &&
            sender.Lobby.Players.Any(p => p.Name == "Observer" && p.Map == MapState.Missing) &&
            sender.Lobby.Players.All(p => !p.Ready), 5), rig.Server.Status());

        LobbySetup actual = rig.Server.Setup;
        Assert.False(rig.Server.Players.Single(p => p.Name == "Player").IsAdmin);
        Assert.Equal(EGameMode.GM_Multi, actual.Mode); // The target does not play Disk Hunt.
        Assert.Equal((13, 2, 5, 7), (actual.TimeLimit, actual.SpyLives, actual.MercLives, actual.RespawnSeconds));
        Assert.Equal(original with
        {
            Map = "AquaE", Mode = EGameMode.GM_Multi, TimeLimit = 13, SpyLives = 2, MercLives = 5,
            RespawnSeconds = 7, GamesInARow = LobbySetup.RevengeGames,
        }, actual);
        Assert.True(rig.RunUntil(() => observer.Lobby.Messages.Any(m => m.Text.StartsWith("Player selected AquaE", StringComparison.Ordinal)), 5));
        rig.RunUntil(() => false, 6);
        Assert.Equal(SessionPhase.Lobby, rig.Server.Phase);
        Assert.Equal(0, rig.Server.RoundsStarted);
        Assert.Same(lobbyHost, rig.Server.Host);
        Assert.Equal(OnlineClientState.Lobby, sender.State);
        Assert.Equal(OnlineClientState.Lobby, observer.State);

        if (adminPassword is not null)
        {
            sender.Chat("!map TestBox");
            Assert.True(rig.RunUntil(() => sender.Lobby.Messages.Any(m => m.Text == "Only the host or an admin may change the map."), 5));
            Assert.Equal("AquaE", rig.Server.Setup.Map);
        }
    }

    [Fact]
    public void CurrentPlayableModeIsKeptAndItsMapDefaultsAreLoaded()
    {
        MapCatalog maps = Maps();
        using OnlineRig rig = Create(maps, out SimLinkNetwork network,
            new LobbySetup { Map = MapCatalog.TestBox, Mode = EGameMode.GM_DeathMatch, TimeLimit = 1 });
        OnlineClient player = Join(rig, network, maps, "Player", 0);
        WaitForLobby(rig, player);
        player.Chat("!AquaE");
        Assert.True(rig.RunUntil(() => player.Lobby.Setup.Map == "AquaE", 5));
        Assert.Equal(EGameMode.GM_DeathMatch, rig.Server.Setup.Mode);
        Assert.Equal((15, 7, 8, 5), (rig.Server.Setup.TimeLimit, rig.Server.Setup.SpyLives,
            rig.Server.Setup.MercLives, rig.Server.Setup.RespawnSeconds));
    }

    [Fact]
    public void ExplicitMapCommandLetsOrdinaryDedicatedPlayerChooseAPlayableMode()
    {
        MapCatalog maps = Maps();
        using OnlineRig rig = Create(maps, out SimLinkNetwork network,
            new LobbySetup { Map = MapCatalog.TestBox, Mode = EGameMode.GM_Multi, TimeLimit = 1 }, adminPassword: "root");
        OnlineClient player = Join(rig, network, maps, "Player", 0);
        WaitForLobby(rig, player);
        player.Chat("!map AquaE Deathmatch");
        Assert.True(rig.RunUntil(() => player.Lobby.Setup.Map == "AquaE" &&
            player.Lobby.Setup.Mode == EGameMode.GM_DeathMatch, 5));
        Assert.False(rig.Server.Players.Single().IsAdmin);
        Assert.Equal((15, 7, 8, 5), (rig.Server.Setup.TimeLimit, rig.Server.Setup.SpyLives,
            rig.Server.Setup.MercLives, rig.Server.Setup.RespawnSeconds));
        Assert.Equal(SessionPhase.Lobby, rig.Server.Phase);
        Assert.Equal(0, rig.Server.RoundsStarted);
    }

    [Theory]
    [InlineData("NoSuchMode", "Unknown mode")]
    [InlineData("DiskHunt", "does not support")]
    public void ExplicitInvalidOrUnplayableModeDoesNotChangeThePendingSetup(string mode, string expectedReply)
    {
        MapCatalog maps = Maps();
        using OnlineRig rig = Create(maps, out SimLinkNetwork network, adminPassword: "root");
        OnlineClient player = Join(rig, network, maps, "Player", 0);
        WaitForLobby(rig, player);
        LobbySetup before = rig.Server.Setup;
        player.Chat("!map AquaE " + mode);
        Assert.True(rig.RunUntil(() => player.Lobby.Messages.Any(m => m.IsSystem &&
            m.Text.Contains(expectedReply, StringComparison.Ordinal)), 5));
        Assert.Equal(before, rig.Server.Setup);
        Assert.Equal(SessionPhase.Lobby, rig.Server.Phase);
    }

    [Theory]
    [InlineData("!AquaE")]
    [InlineData("!map AquaE")]
    public void OperatorCanDisableOrdinaryShortcutsWhileAdminsCanStillSelectMaps(string command)
    {
        MapCatalog maps = Maps();
        using OnlineRig rig = Create(maps, out SimLinkNetwork network, adminPassword: "root", openMapCommands: false);
        OnlineClient player = Join(rig, network, maps, "Player", 0);
        WaitForLobby(rig, player);
        LobbySetup before = rig.Server.Setup;
        player.Chat(command);
        Assert.True(rig.RunUntil(() => player.Lobby.Messages.Any(m => m.Text == "Only the host or an admin may change the map."), 5));
        Assert.Equal(before, rig.Server.Setup);

        player.Admin("root");
        Assert.True(rig.RunUntil(() => player.Lobby.Players.Any(p => p.Name == "Player" && p.IsAdmin), 5));
        player.Chat(command);
        Assert.True(rig.RunUntil(() => player.Lobby.Setup.Map == "AquaE", 5));
        Assert.Equal(SessionPhase.Lobby, rig.Server.Phase);
    }

    [Theory]
    [InlineData("!NoSuchMap")]
    [InlineData("!Lobby")]
    [InlineData("!TSP20")]
    [InlineData("!TestBox")]
    [InlineData("!Aqua")]
    [InlineData("!AquaE extra")]
    public void UnknownNonHostableAndNonExactCommandsStayChatWithoutChangingSetup(string text)
    {
        MapCatalog maps = Maps();
        using OnlineRig rig = Create(maps, out SimLinkNetwork network);
        OnlineClient player = Join(rig, network, maps, "Player", 0);
        WaitForLobby(rig, player);
        LobbySetup before = rig.Server.Setup;
        player.Chat(text);
        Assert.True(rig.RunUntil(() => player.Lobby.Messages.Any(m => m.Sender == "Player" && m.Text == text), 5));
        Assert.Equal(before, rig.Server.Setup);
        Assert.Equal(SessionPhase.Lobby, rig.Server.Phase);
    }

    [Theory]
    [InlineData("!AquaE")]
    [InlineData("!map AquaE")]
    public void MapRemovedAfterCatalogWasReadCannotBeSelected(string command)
    {
        MapCatalog maps = Maps();
        Assert.NotNull(maps.GetCatalogEntry("AquaE"));
        File.Delete(Path.Combine(_root, "AquaE", "manifest.json"));
        using OnlineRig rig = Create(maps, out SimLinkNetwork network);
        OnlineClient player = Join(rig, network, maps, "Player", 0);
        WaitForLobby(rig, player);
        LobbySetup before = rig.Server.Setup;
        player.Chat(command);
        Assert.True(rig.RunUntil(() => player.Lobby.Messages.Any(m => m.Text == "Map 'AquaE' is not installed on this server."), 5));
        Assert.Equal(before, rig.Server.Setup);
    }

    [Fact]
    public void ShortcutDoesNotChangeListenHostLobbies()
    {
        MapCatalog maps = Maps();
        using OnlineRig rig = Create(maps, out SimLinkNetwork network, dedicated: false);
        OnlineClient player = Join(rig, network, maps, "Guest", 0);
        WaitForLobby(rig, player);
        LobbySetup before = rig.Server.Setup;
        player.Chat("!AquaE");
        Assert.True(rig.RunUntil(() => player.Lobby.Messages.Any(m => m.Sender == "Guest" && m.Text == "!AquaE"), 5));
        Assert.Equal(before, rig.Server.Setup);
    }

    [Fact]
    public void ShortcutDoesNotInterruptARunningMatch()
    {
        MapCatalog maps = Maps();
        using OnlineRig rig = Create(maps, out SimLinkNetwork network);
        OnlineClient spy = Join(rig, network, maps, "Spy", 0);
        OnlineClient merc = Join(rig, network, maps, "Merc", 1);
        WaitForLobby(rig, spy, merc);
        spy.SetReady(true);
        merc.SetReady(true);
        Assert.True(rig.RunUntil(() => rig.Server.Players.All(p => p.Ready), 5), rig.Server.Status());
        spy.Launch();
        Assert.True(rig.RunUntil(() => spy.State == OnlineClientState.Match && merc.State == OnlineClientState.Match, 20), rig.Server.Status());
        LobbySetup before = rig.Server.Setup;
        spy.Chat("!AquaE");
        rig.RunUntil(() => false, 1);
        Assert.Equal(before, rig.Server.Setup);
        Assert.Equal(SessionPhase.Match, rig.Server.Phase);
        Assert.Equal(1, rig.Server.RoundsStarted);
    }

    private MapCatalog Maps()
    {
        WriteMap("AquaE");
        WriteMap("TSP20");
        string fixtures = Path.Combine(MapCatalog.FindRepoRoot(AppContext.BaseDirectory)!, "fixtures");
        return new MapCatalog(_root, fixtures);
    }

    private static OnlineRig Create(MapCatalog maps, out SimLinkNetwork network, LobbySetup? setup = null,
        string? adminPassword = null, bool openMapCommands = true, bool dedicated = true, bool autoLaunch = false)
    {
        var clock = new VirtualClock();
        network = new SimLinkNetwork(clock, 37, SimLinkProfile.A);
        var settings = OnlineRig.Settings(MapCatalog.TestBox) with
        {
            Maps = maps, Setup = setup ?? new LobbySetup { Map = MapCatalog.TestBox },
            MaxPlayers = setup?.MaxPlayers ?? 4, Dedicated = dedicated, AutoLaunch = autoLaunch,
            OpenMapCommands = openMapCommands, AdminPassword = adminPassword, LaunchCountdownSeconds = 5,
        };
        return new OnlineRig(new OnlineServer(network.Bind("server"), settings, clock.NowMicroseconds), clock);
    }

    private static OnlineClient Join(OnlineRig rig, SimLinkNetwork network, MapCatalog maps, string name, int team)
    {
        int connection = 0;
        var target = JoinTarget.Custom("Shortcut server", () => network.Bind(name + "-" +
            (connection++).ToString(CultureInfo.InvariantCulture)), "server");
        return rig.Add(new OnlineClient(target, OnlineRig.ClientSettings(name, team) with { Maps = maps }, rig.Now));
    }

    private static void WaitForLobby(OnlineRig rig, params OnlineClient[] clients) =>
        Assert.True(rig.RunUntil(() => clients.All(c => c.State == OnlineClientState.Lobby &&
            c.Lobby.Players.Count == clients.Length && c.Lobby.Players.All(p => p.Map == MapState.Ok)), 10), rig.Server.Status());

    private void WriteMap(string id)
    {
        string folder = Path.Combine(_root, id);
        Directory.CreateDirectory(Path.Combine(folder, "sim"));
        File.WriteAllBytes(Path.Combine(folder, "sim", "collision.bin"), CollisionWriter.Write(CollisionData.Empty));
        File.WriteAllText(Path.Combine(folder, "sim", "traversal.json"), $$"""{"formatVersion":"1.0.0","map":"{{id}}","ges":[]}""");
        File.WriteAllText(Path.Combine(folder, "sim", "gameplay.json"), $$$"""
            {"formatVersion":"1.0.0","map":"{{{id}}}",
             "levelInfo":{"class":"Engine.LevelInfo","id":"LevelInfo0","props":{"GEActivationMask":0}},
             "actors":[{"class":"Engine.PlayerStart","id":"SpyStart","props":{"TeamNumber":0}},
                       {"class":"Engine.PlayerStart","id":"MercStart","props":{"TeamNumber":1}}],
             "mapSettings":{
               "Story":{"GameModeId":5,"TimeLimit":13,"SpiesLives":2,"SentriesLives":5,"RespawnTime":7},
               "Deathmatch":{"GameModeId":6,"TimeLimit":15,"SpiesLives":7,"SentriesLives":8,"RespawnTime":4}},
             "tagIndex":{},"eventEdges":[],
             "missionTree":[{"mission":"Mission0","gameMode":5,"minimumObjectives":1,
                             "objectives":[{"objective":"Objective0","triggers":[]}]}]}
            """);
        string simHash = SvMapHash.ComputeSimHash(folder);
        File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(new
        {
            format = "svmap", formatVersion = "1.0.0", map = id, title = id,
            files = new { }, hashes = new { sim = simHash }, requires = Array.Empty<string>(),
            source = new { generator = "test", generatorVersion = 1, kind = "converted",
                original = new { file = "Packages/Maps/" + id + ".sdc", sha256 = new string('0', 64) } },
        }));
    }
}
