using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Game;

/// <summary>An actor that counts the events it receives.</summary>
internal sealed class EventProbe : Actor
{
    public int Triggers;

    protected override void Trigger(Actor? Other, Pawn? EventInstigator) => Triggers++;
}

/// <summary>A mission that records the calls StartMatch makes (the mission rules belong to the objectives port).</summary>
internal sealed class RecordingMission : SMission
{
    public int Resets;
    public int Deactivations;

    public override void Reset() => Resets++;

    public override void Desactivate() => Deactivations++;
}

/// <summary>spec/game/match-lifecycle.md: setup order, the start gate, StartMatch and the rules timer.</summary>
public class MatchLifecycleTests
{
    internal const string SteeEStory = "?MapName=SteeE?TimeLimit=10?GameMode=5?LivesATT=4?TimesRespawnsATT=10?LivesDEF=3";

    internal static void TwoSides(GameWorld w)
    {
        w.AddStart(new FVector(0f, 0f, 0f), 0);
        w.AddStart(new FVector(200f, 0f, 0f), 0);
        w.AddStart(new FVector(5000f, 0f, 0f), 1);
        w.AddStart(new FVector(5200f, 0f, 0f), 1);
    }

    // ---- setup (rules 1 to 4) ---------------------------------------------------------------------------------

    [Fact]
    public void Setup_spawns_the_replication_info_with_the_match_settings_and_two_teams()
    {
        var w = new GameWorld();
        w.Load(SteeEStory + "?GamesInARow=2?Revenge=true", TwoSides);
        GameReplicationInfo gri = w.Gri;
        Assert.Same(gri, w.Level.Info.GRI);
        Assert.Equal(10, gri.TimeLimit);
        Assert.Equal(4, gri.TeamLivesATT);
        Assert.Equal(3, gri.TeamLivesDEF);
        Assert.Equal(10, gri.TimesRespawn);
        Assert.Equal(EGameMode.GM_Multi, gri.GameMode);
        Assert.True(gri.Revenge);
        Assert.Equal(2, gri.GamesInARow);
        Assert.Equal("SteeE", gri.ServerName);
        Assert.IsType<STeamAttaquant>(gri.Teams[0]);
        Assert.IsType<STeamDefenseur>(gri.Teams[1]);
        Assert.Equal(0, gri.Teams[0]!.TeamIndex);
        Assert.Equal(1, gri.Teams[1]!.TeamIndex);
        Assert.Equal(0, gri.NumDogTagsTaken);
        Assert.Equal(1f, w.Game.TimerRate);
        Assert.True(w.Game.bTimerLoop);
    }

    [Fact]
    public void The_host_persistent_settings_reach_the_replication_info()
    {
        var w = new GameWorld();
        w.Level.BeginLoad();
        string error = string.Empty;
        var game = new SGameInfo();
        game.PersistentData.bDedicatedServer = true;
        game.PersistentData.NumDisks = 7;
        w.Level.SpawnActor(game, default, default);
        w.Level.Info.Game = game;
        game.InitGame(SteeEStory, ref error);
        w.Level.EndLoad();
        Assert.True(game.GameReplicationInfo!.ServerOnly);
        Assert.Equal(7, game.GameReplicationInfo.NumDisks);
    }

    // ---- the start gate (rules 5 to 13) -----------------------------------------------------------------------

    [Fact]
    public void A_network_login_waits_without_a_pawn_until_the_host_starts()
    {
        GameWorld w = new GameWorld().Load(SteeEStory, TwoSides);
        SPlayerController host = w.Join("?Name=Host?Team=0", local: true);
        SPlayerController client = w.Join("?Name=Bob?Team=1");
        Assert.True(host.IsInState("s_PlayerWaiting"));
        Assert.True(client.IsInState("s_PlayerWaiting"));
        Assert.Null(host.Pawn);
        Assert.False(w.Gri.bMatchHasBegun);
        Assert.Equal(2, w.Host.Named("ClientSetHUD").Count);
    }

    [Fact]
    public void StartMatchOnServer_sets_bMatchHasBegun_before_the_players_spawn_and_assigns_lives_per_side()
    {
        GameWorld w = new GameWorld().Load(SteeEStory, TwoSides);
        SPlayerController host = w.Join("?Name=Host?Team=0", local: true);
        SPlayerController client = w.Join("?Name=Bob?Team=1");
        w.StartNetworkMatch(host);

        Assert.True(w.Gri.bMatchHasBegun);
        Assert.IsType<SPawnAttaque_1Mesh>(host.Pawn);
        Assert.IsType<SPawnDefense_1Mesh>(client.Pawn);
        Assert.Equal(4, host.PlayerReplicationInfo!.NumLives);
        Assert.Equal(3, client.PlayerReplicationInfo!.NumLives);
        Assert.Equal(4, w.Gri.TotalTeamLives[0]);
        Assert.Equal(3, w.Gri.TotalTeamLives[1]);
        Assert.True(w.Gri.bMatchLaunching);
        Assert.Equal(10, w.Gri.TimesRespawn);
        Assert.Equal(255, w.Gri.WinningTeam);
        Assert.Equal(255, w.Gri.WinningReason);
        Assert.False(w.Gri.bStopCountDown);
        Assert.Equal(0, w.Gri.ElapsedTime);
        Assert.Equal(600, w.Gri.RemainingMinute);
        Assert.False(w.Game.bWaitingToStartMatch);
        Assert.True(w.Game.bWelcomePending);
        Assert.Equal(2, w.Host.Named("resetConnectionPing").Count);
    }

    [Fact]
    public void Play_level_starts_from_the_first_login_and_never_sets_bMatchHasBegun()
    {
        GameWorld w = new GameWorld(ENetMode.NM_Standalone).Load("?GameMode=5?Team=0?Editeur=true", TwoSides);
        SPlayerController ed = w.Join("?Name=Ed?Team=0?Editeur=true", local: true);
        Assert.NotNull(ed.Pawn);
        Assert.False(w.Gri.bMatchHasBegun);
        Assert.False(w.Game.bDelayedStart);
        Assert.False(w.Game.bWaitingToStartMatch);
        Assert.Equal(999999, ed.PlayerReplicationInfo!.NumLives);
        Assert.Single(w.Host.ScriptEvents);
        Assert.Equal(3, w.Host.ScriptEvents[0].Id);
        Assert.Equal(1, w.Ports.Count("SpawnMagmaHud"));
    }

    [Fact]
    public void An_offline_match_from_the_menus_sets_bMatchHasBegun_at_the_end_of_StartMatch()
    {
        var w = new GameWorld(ENetMode.NM_Standalone);
        w.Host.GUIInGame = true;
        w.Load("?GameMode=5?TimeLimit=0?LivesATT=0?LivesDEF=0?TimesRespawnsATT=5", TwoSides);
        SPlayerController p = w.Join("?Name=Solo?Team=0", local: true);
        Assert.Null(p.Pawn);
        w.Game.bDelayedStart = false;
        w.Game.StartMatch();
        Assert.NotNull(p.Pawn);
        Assert.True(w.Gri.bMatchHasBegun);
    }

    [Theory]
    [InlineData("?GameMode=5", 1, 0, 1)]
    [InlineData("?GameMode=4", 0, 1, 9)]
    [InlineData("?GameMode=7", 0, 0, 1)]
    public void StartMatch_deactivates_every_mission_and_resets_those_of_the_current_mode(
        string mode, int storyResets, int diskResets, int diskMinimum)
    {
        RecordingMission? story = null;
        RecordingMission? diskHunt = null;
        GameWorld w = new GameWorld().Load(mode, x =>
        {
            TwoSides(x);
            story = x.Level.SpawnActor(new RecordingMission { GameMode = EGameMode.GM_Multi, MinimumObjectives = 1 }, default, default);
            diskHunt = x.Level.SpawnActor(new RecordingMission { GameMode = EGameMode.GM_DiskHunt, MinimumObjectives = 1 }, default, default);
        });
        w.Game.PersistentData.NumDisks = 9;
        w.Gri.NumObjectives = 5;
        SPlayerController host = w.Join("?Name=Host?Team=0", local: true);
        w.StartNetworkMatch(host);

        Assert.Equal(1, story!.Deactivations);
        Assert.Equal(1, diskHunt!.Deactivations);
        Assert.Equal(storyResets, story.Resets);
        Assert.Equal(diskResets, diskHunt.Resets);
        Assert.Equal(diskMinimum, diskHunt.MinimumObjectives);
        Assert.Equal(0, w.Gri.NumObjectives);
    }

    [Fact]
    public void A_player_without_a_pawn_keeps_zero_lives_outside_merc_deathmatch()
    {
        GameWorld w = new GameWorld().Load(SteeEStory, TwoSides);
        SPlayerController host = w.Join("?Name=Host?Team=0", local: true);
        host.PawnClass = default;
        w.StartNetworkMatch(host);
        Assert.Null(host.Pawn);
        Assert.Equal(0, host.PlayerReplicationInfo!.NumLives);
        Assert.True(host.PlayerReplicationInfo!.bOnlySpectator);
    }

    [Fact]
    public void launchMatchNow_sets_the_lobby_countdown_flags()
    {
        GameWorld w = new GameWorld().Load(SteeEStory, TwoSides);
        SPlayerController host = w.Join("?Name=Host?Team=0", local: true);
        host.launchMatchNow();
        Assert.True(w.Gri.bMatchLaunchingNow);
        Assert.True(w.Gri.bMatchLaunching);

        SPlayerController remote = w.Join("?Name=Bob?Team=1");
        remote.launchMatchNow();
        Assert.Single(w.Host.To(remote, "launchMatchNow"));
    }

    [Fact]
    public void PlayerWaitingToPlayToServer_marks_the_player_waiting()
    {
        GameWorld w = new GameWorld().Load(SteeEStory, TwoSides);
        SPlayerController p = w.Join("?Name=Bob?Team=1");
        p.PlayerWaitingToPlayToServer(true);
        Assert.True(p.PlayerReplicationInfo!.bWaitingPlayer);
        Assert.Null(p.Pawn);
    }

    [Fact]
    public void PostLogin_sends_the_HUD_and_scoreboard_classes_and_flags_the_LAN_host()
    {
        GameWorld w = new GameWorld().Load(SteeEStory, TwoSides);
        SPlayerController host = w.Join("?Name=H?Team=0?LAN=Server", local: true);
        GameCall hud = Assert.Single(w.Host.To(host, "ClientSetHUD"));
        Assert.Equal("SBase.SHud", hud.Class.Path);
        Assert.Equal("SBase.SScoreBoardStory", hud.Class2.Path);
        Assert.True(host.PlayerReplicationInfo!.bIAmServer);
        Assert.Equal("Server", w.Game.InLAN);
    }

    [Fact]
    public void Login_reads_the_punkbuster_option_the_map_name_and_spawns_the_security_object()
    {
        var w = new GameWorld();
        w.Level.BeginLoad();
        TwoSides(w);
        string error = string.Empty;
        var game = new SGameInfo { LevelSettings = new USMapSettings { MapName = "ENHANCED STEEL SQUAT" } };
        w.Level.SpawnActor(game, default, default);
        w.Level.Info.Game = game;
        game.InitGame(SteeEStory, ref error);
        w.Level.EndLoad();
        PlayerController? p = game.SpawnPlayActor(string.Empty, "?Name=A?Team=0?PB=1?NewProfile=true", false, ref error);
        var spc = Assert.IsType<SPlayerController>(p);
        Assert.Equal(1, spc.iPBEnabled);
        Assert.True(spc.bNewProfile);
        Assert.NotNull(spc.PlayerSecurity);
        Assert.Same(game.GameReplicationInfo, spc.GameReplicationInfo);
        Assert.Equal("ENHANCED STEEL SQUAT", game.GameReplicationInfo!.m_ActualMapName);
    }

    [Fact]
    public void StartMatch_after_the_end_spawns_nobody_but_still_restarts_the_clock()
    {
        GameWorld w = new GameWorld().Load(SteeEStory, TwoSides);
        SPlayerController p = w.Join("?Name=A?Team=0");
        w.Game.bGameEnded = true;
        w.Gri.WinningTeam = 1;
        w.Gri.ElapsedTime = 40;
        w.Game.bDelayedStart = false;
        w.Game.StartMatch();
        Assert.Null(p.Pawn);
        Assert.Equal(1, w.Gri.WinningTeam);
        Assert.Equal(0, w.Gri.ElapsedTime);
    }

    // ---- the rules timer (rules 29 to 34) ---------------------------------------------------------------------

    [Fact]
    public void New_spectators_are_welcomed_once_in_network_games_but_not_the_non_playing_host()
    {
        var w = new GameWorld();
        w.Load(SteeEStory, TwoSides);
        SPlayerController host = w.Join("?Name=Host?ServerOnly=true?LAN=Server", local: true);
        Assert.True(host.PlayerReplicationInfo!.bOnlySpectator);
        w.Gri.ServerOnly = true;
        w.Run(1.1f);
        Assert.True(host.PlayerReplicationInfo!.bWelcomed);
        Assert.Empty(w.Host.Messages(1));

        SPlayerController p = w.Join("?Name=Bob?Team=0");
        w.StartNetworkMatch(p);
        SPlayerController late = w.Join("?Name=Late?Team=1");
        Assert.True(late.PlayerReplicationInfo!.bOnlySpectator);
        w.Run(1.1f);
        Assert.Contains(w.Host.Messages(1), m => ReferenceEquals(m.Call.Pri1, late.PlayerReplicationInfo));
        Assert.Equal(3, w.Host.Messages(1).FindAll(m => ReferenceEquals(m.Call.Pri1, p.PlayerReplicationInfo)).Count);
        Assert.Equal(3, w.Host.Messages(1).FindAll(m => ReferenceEquals(m.Call.Pri1, late.PlayerReplicationInfo)).Count);
        w.Host.Calls.Clear();
        w.Run(1.1f);
        Assert.Empty(w.Host.Messages(1));
    }

    [Fact]
    public void Offline_games_send_no_welcome_message()
    {
        GameWorld w = new GameWorld(ENetMode.NM_Standalone).Load(SteeEStory, TwoSides);
        w.Join("?Name=Host?ServerOnly=true", local: true);
        w.Run(1.1f);
        Assert.Empty(w.Host.Messages(1));
    }

    [Fact]
    public void A_positive_SendAtTime_fires_the_level_event_once_after_that_many_seconds()
    {
        EventProbe? probe = null;
        GameWorld w = new GameWorld().Load(SteeEStory, x =>
        {
            TwoSides(x);
            probe = x.Level.SpawnActor(new EventProbe { Tag = "Boom" }, default, default);
            x.Level.Info.Event = "Boom";
            x.Level.Info.SendAtTime = 3;
        });
        w.Run(2.5f);
        Assert.Equal(0, probe!.Triggers);
        w.Run(2f);
        Assert.Equal(1, probe.Triggers);
        Assert.Equal(0, w.Level.Info.SendAtTime);
        w.Run(3f);
        Assert.Equal(1, probe.Triggers);
    }

    [Fact]
    public void A_negative_SendAtTime_fires_when_the_remaining_time_drops_below_it()
    {
        EventProbe? probe = null;
        GameWorld w = new GameWorld().Load("?GameMode=5?TimeLimit=1", x =>
        {
            TwoSides(x);
            probe = x.Level.SpawnActor(new EventProbe { Tag = "Boom" }, default, default);
            x.Level.Info.Event = "Boom";
            x.Level.Info.SendAtTime = -5;
        });
        SPlayerController host = w.Join("?Name=Host?Team=0", local: true);
        w.StartNetworkMatch(host);
        w.Run(50f);
        Assert.Equal(0, probe!.Triggers);
        w.Run(6.5f);
        Assert.Equal(1, probe.Triggers);
    }

    [Fact]
    public void The_rules_timer_drains_poison_for_poisoned_players()
    {
        GameWorld w = new GameWorld().Load(SteeEStory, TwoSides);
        SPlayerController p = w.Join("?Name=Bob?Team=0");
        p.PoisonInstigator = p;
        w.Run(1.1f);
        Assert.Equal(1, w.Ports.Count("CountPoisonDrain"));
    }
}
