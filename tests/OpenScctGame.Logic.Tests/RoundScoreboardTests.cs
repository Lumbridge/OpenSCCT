using System.Collections.Generic;
using System;
using System.Linq;
using OpenScct.Net.Scoreboards;
using OpenScctGame.Controls;
using OpenScctGame.UI;
using OpenScctGame.Options;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class RoundScoreboardTests
{
    [Fact]
    public void Hold_sources_and_toggle_are_independent_and_reset_clears_everything()
    {
        var input = new ScoreboardInputState();
        Assert.False(input.Visible);
        input.Hold("keyboard", true); input.Hold("mouse", true);
        input.Hold("keyboard", false); Assert.True(input.Visible);
        input.Toggle(); input.Hold("mouse", false); Assert.True(input.Visible);
        input.Toggle(); Assert.False(input.Visible);
        input.Hold("pad", true); input.Toggle(); input.Clear();
        Assert.False(input.Visible); Assert.False(input.Toggled);
        input.Hold("pad", false); Assert.False(input.Visible);
    }

    [Fact]
    public void Default_keys_keep_Tab_vision_and_F10_toggle_with_hold_unbound()
    {
        var layout = DefaultKeyLayout.Create();
        Assert.Contains(layout.Resolve("Tab"), action => action.Name == "SwitchHeadset" && action.Arg == 3);
        Assert.Contains(layout.Resolve("F10"), action => action.Name == "ShowConnectedPlayers");
        Assert.DoesNotContain(layout.Bindings.Values, command => command.Contains("HoldScoreboard"));
        Assert.True(ControlActions.Direct(ControlActions.All.Single(a => a.Id == "players")));
        Assert.True(ControlActions.Direct(ControlActions.All.Single(a => a.Id == "scoreboard_hold")));
    }

    [Fact]
    public void Pending_legacy_and_versioned_zero_are_distinct()
    {
        var board = new RoundScoreboard();
        var pending = new ScoreboardPlayer();
        Assert.Equal("Pending", RoundScoreboardModel.Stat(board, pending, "spy.grab_kos"));
        Assert.Equal("—", RoundScoreboardModel.Stat(board, pending with { StatsVersion = 0 }, "spy.grab_kos"));
        Assert.Equal("—", RoundScoreboardModel.Stat(board with { Partial = true }, pending, "spy.grab_kos"));
        Assert.Equal("0", RoundScoreboardModel.Stat(board, pending with { Stats = new Dictionary<string, int>() }, "spy.grab_kos"));
        Assert.Equal("—", RoundScoreboardModel.Stat(board, pending with { StatsVersion = 0, Stats = new Dictionary<string, int>() }, "spy.grab_kos"));
    }

    [Fact]
    public void Spy_choke_kills_do_not_stand_in_for_missing_grab_knockouts()
    {
        var board = new RoundScoreboard { Completed = true };
        var legacy = new ScoreboardPlayer { StatsVersion = 0, Stats = new Dictionary<string, int> { ["spy.chokes"] = 3 } };
        var details = RoundScoreboardModel.Details(board, legacy);
        Assert.Equal("3", details.Single(d => d.Label == "Choke kills").Value);
        Assert.Equal("—", details.Single(d => d.Label == "Neck-grab knockouts").Value);
    }

    [Fact]
    public void Own_team_first_and_stable_participant_order_do_not_reorder_on_counter_updates()
    {
        var board = new RoundScoreboard { Rows = [new() { ParticipantId = 9, Team = 1 }, new() { ParticipantId = 4, Team = 0 }, new() { ParticipantId = 1, Team = 1, Score = 100 }] };
        Assert.Equal(1, RoundScoreboardModel.FirstTeam(board, 9));
        Assert.Equal(new[] { 1, 9 }, RoundScoreboardModel.Team(board, 1).Select(p => p.ParticipantId));
        Assert.Equal(0, RoundScoreboardModel.FirstTeam(board, -1));
        Assert.Equal("∞", RoundScoreboardModel.Lives(new() { Lives = -1 }));
    }

    [Fact]
    public void Stopped_server_round_and_early_client_departure_have_distinct_finality_labels()
    {
        var stopped = new RoundScoreboard { Partial = true, ElapsedSeconds = 120, AsOfSeconds = 120, EndedUtc = DateTimeOffset.UtcNow };
        Assert.Equal("Round stopped · final totals", RoundScoreboardModel.Timing(stopped));
        Assert.Equal("Partial record · combat totals through 01:30", RoundScoreboardModel.Timing(stopped with { AsOfSeconds = 90 }));
        Assert.Equal("Partial record · no combat sample was released before leaving", RoundScoreboardModel.Timing(stopped with { AsOfSeconds = null }));
        Assert.StartsWith("Partial record", RoundScoreboardModel.Timing(stopped with { EndedUtc = null }));
        Assert.Equal("60:00", RoundScoreboardModel.Clock(3600));
        Assert.Equal("00:00", RoundScoreboardModel.Clock(double.NaN));
    }

    [Fact]
    public void Legacy_compact_roster_settings_are_ignored_and_not_saved_again()
    {
        var settings = GameSettings.FromJson("""
            { "show_scoreboard": false, "show_ping": true, "scoreboard_split_team_size": 4,
              "hud_layout": { "Spy": { "Scoreboard": { "Visible": false, "Scale": 3 } } } }
            """);
        Assert.True(settings.ShowScoreboard);
        Assert.False(settings.ShowPing);
        Assert.Equal(3, settings.ScoreboardSplitTeamSize);
        Assert.False(settings.HudLayout.Spy.ContainsKey("Scoreboard"));
        string saved = settings.ToJson();
        Assert.DoesNotContain("\"show_scoreboard\"", saved);
        Assert.DoesNotContain("\"show_ping\"", saved);
        Assert.DoesNotContain("\"scoreboard_split_team_size\"", saved);
        Assert.False(new ScoreboardInputState().Visible);
    }

    [Fact]
    public void Unknown_legacy_lives_objectives_and_ping_are_not_presented_as_real_values()
    {
        var player = new ScoreboardPlayer { StatsVersion = 0, Lives = -2, Objectives = -1, PingMs = -1 };
        var board = new RoundScoreboard { Completed = true };
        Assert.Equal("—", RoundScoreboardModel.Lives(player));
        Assert.Equal("—", RoundScoreboardModel.Objectives(player));
        Assert.Equal("—", RoundScoreboardModel.Columns(0).Single(c => c.Label == "Ping").Read(board, player));
        Assert.Equal("—", RoundScoreboardModel.Details(board, player).Single(d => d.Label == "Objectives completed").Value);
        Assert.Equal("∞", RoundScoreboardModel.Lives(player with { Lives = -1 }));
    }

    [Fact]
    public void Passive_board_keeps_all_24_active_players_and_only_fills_spare_slots_with_departed_rows()
    {
        var players = Enumerable.Range(0, 27).Select(i => new ScoreboardPlayer { ParticipantId = i, Team = 1, LeftEarly = i < 3 }).ToArray();
        var board = new RoundScoreboard { Rows = players };
        var visible = RoundScoreboardModel.LiveRows(board);
        Assert.Equal(24, visible.Count);
        Assert.DoesNotContain(visible, p => p.LeftEarly);
        Assert.Equal(Enumerable.Range(3, 24), visible.Select(p => p.ParticipantId));
        visible = RoundScoreboardModel.LiveRows(board with { Rows = players.Take(25).ToArray() });
        Assert.Equal(24, visible.Count);
        Assert.Equal(new[] { 0, 1 }, visible.Where(p => p.LeftEarly).Select(p => p.ParticipantId));
        Assert.Equal(27, RoundScoreboardModel.Team(board, 1).Count);
    }
}
