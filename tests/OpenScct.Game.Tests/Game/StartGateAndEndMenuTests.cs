using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.GameFlow;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Game;

/// <summary>
/// spec/game/match-lifecycle.md rules 8 to 12 (the loading page's start gate) and spec/game/round-travel.md rules 1
/// and 2 (the end menu's travel).
/// </summary>
public class StartGateAndEndMenuTests
{
    private const string Story = MatchLifecycleTests.SteeEStory;

    [Fact]
    public void A_listen_host_starts_one_second_after_every_player_reported_waiting()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController host = w.Join("?Name=H?Team=0", local: true);
        SPlayerController client = w.Join("?Name=C?Team=1");
        var gate = w.Level.Spawn<LoadingMenuStartGate>()!;
        Assert.True(gate.IsInState("s_Init"));

        w.Run(5f);
        Assert.True(gate.IsInState("s_Server"));
        Assert.True(host.PlayerReplicationInfo!.bWaitingPlayer);
        Assert.False(w.Gri.bMatchHasBegun);
        Assert.True(gate.TimeOut >= 4);

        client.PlayerWaitingToPlayToServer(true);
        w.Run(2.1f);
        Assert.True(w.Gri.bMatchHasBegun);
        Assert.NotNull(host.Pawn);
        Assert.NotNull(client.Pawn);
        Assert.True(gate.bDeleteMe);
    }

    [Fact]
    public void With_clients_still_connecting_the_gate_waits_for_the_all_in_event()
    {
        var w = new GameWorld();
        w.Host.Pending = 1;
        w.Load(Story, MatchLifecycleTests.TwoSides);
        SPlayerController host = w.Join("?Name=H?Team=0", local: true);
        var gate = w.Level.Spawn<LoadingMenuStartGate>()!;
        w.Run(4f);
        Assert.Equal(0, gate.TimeOut);
        gate.NotifyAllClientsInMap();
        w.Run(3.1f);
        Assert.True(w.Gri.bMatchHasBegun);
        Assert.NotNull(host.Pawn);
    }

    [Fact]
    public void A_non_playing_host_is_not_waited_for()
    {
        GameWorld w = new GameWorld().Load(Story, MatchLifecycleTests.TwoSides);
        w.Game.PersistentData.bDedicatedServer = true;
        SPlayerController host = w.Join("?Name=H?ServerOnly=true", local: true);
        SPlayerController client = w.Join("?Name=C?Team=1");
        var gate = w.Level.Spawn<LoadingMenuStartGate>()!;
        w.Run(1.5f);
        Assert.False(w.Gri.bMatchHasBegun);

        // Only PRIs after the host's (index 0) must wait.
        host.PlayerReplicationInfo!.bWaitingPlayer = false;
        client.PlayerWaitingToPlayToServer(true);
        w.Run(2.1f);
        Assert.Equal(1, gate.NumFirstPlayer);
        Assert.True(w.Gri.bMatchHasBegun);
        Assert.Null(host.Pawn);
        Assert.NotNull(client.Pawn);
    }

    [Fact]
    public void An_offline_match_starts_after_two_seconds_before_bMatchHasBegun()
    {
        var w = new GameWorld(ENetMode.NM_Standalone);
        w.Load("?GameMode=5", x =>
        {
            x.AddStart(new FVector(0f, 0f, 0f), 0, priority: 1);
            x.AddStart(new FVector(500f, 0f, 0f), 0);
        });
        SPlayerController p = w.Join("?Name=S?Team=0", local: true);
        w.Level.Spawn<LoadingMenuStartGate>();
        w.Run(1.9f);
        Assert.Null(p.Pawn);
        w.Run(0.7f);
        Assert.NotNull(p.Pawn);
        Assert.Equal(0f, p.StartSpot!.Location.X); // the priority start: the offline spawn ran before bMatchHasBegun
    }

    // ---- the end menu (round-travel.md rules 1 and 2) ---------------------------------------------------------

    private static (GameWorld W, SPlayerController Host, SPlayerController Other) Pair(string options)
    {
        GameWorld w = new GameWorld().Load(options, MatchLifecycleTests.TwoSides);
        w.Game.PersistentData.LevelName = "SteeE";
        SPlayerController host = w.Join("?Name=H?Team=0", local: true);
        SPlayerController other = w.Join("?Name=O?Team=1");
        return (w, host, other);
    }

    [Fact]
    public void Nobody_travels_until_every_player_is_ready()
    {
        (GameWorld w, SPlayerController host, SPlayerController other) = Pair(Story + "?GamesInARow=3");
        host.PlayerReadyToPlayToServer(true);
        Assert.False(EndingMenuRules.TryTravel(w.Game, host, 1));
        other.PlayerReadyToPlayToServer(true);
        Assert.True(EndingMenuRules.TryTravel(w.Game, host, 1));
        Assert.StartsWith("SteeE?MapName=SteeE", w.Level.Info.NextURL);
    }

    [Fact]
    public void The_next_game_of_a_series_has_one_game_less_and_revenge_carries_the_points_swapped()
    {
        (GameWorld w, _, _) = Pair(Story + "?Revenge=true?GamesInARow=2");
        w.Level.Info.NbPointsAttack = 12;
        w.Level.Info.NbPointsDefense = 30;
        string url = EndingMenuRules.NextRoundUrl(w.Game, 1);
        Assert.StartsWith("SteeE?MapName=SteeE?", url);
        Assert.EndsWith("?GamesInARow=2?TimesReinitFlag=1?TimeToExplode=1?GameWithRanking=False?LevelGap=0?ScoreDEF=12?ScoreATT=30?GamesInARow=1", url);
    }

    [Fact]
    public void The_series_ends_in_the_lobby_after_the_last_game_or_when_a_side_is_empty()
    {
        (GameWorld w, _, SPlayerController other) = Pair(Story + "?GamesInARow=1");
        string url = EndingMenuRules.NextRoundUrl(w.Game, 4);
        Assert.StartsWith("Lobby?MapName=SteeE", url);
        Assert.Contains("?GamesInARow=4", url);
        Assert.Equal(4, w.Gri.GamesInARow);

        (GameWorld w2, _, SPlayerController other2) = Pair(Story + "?GamesInARow=3");
        w2.Game.eventChangeTeam(other2, 0, false);
        Assert.StartsWith("Lobby", EndingMenuRules.NextRoundUrl(w2.Game, 1));
        Assert.Equal(2, EndingMenuRules.HowManyPlayersInTeam(w2.Gri, 0));
        Assert.Same(other, other);
    }
}
