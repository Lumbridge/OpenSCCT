using System.Collections.Generic;
using OpenScctGame.Hud;

namespace OpenScctGame.UI;

/// <summary>How a round ended (spec/game/end-of-round.md).</summary>
public enum MatchEndReason
{
    /// <summary>END_STORY: the spies took the objectives needed.</summary>
    Story,

    /// <summary>END_TIMEOUT: the time limit ran out (the mercs win on time in Story).</summary>
    Timeout,

    /// <summary>END_TEAMDEATH: a side has no player with lives left.</summary>
    TeamDeath,

    /// <summary>The match was left or stopped before an end.</summary>
    Aborted,
}

/// <summary>One line of the results table.</summary>
public sealed record MatchResultRow(string Name, HudSide Side, int Score, int Kills, int Deaths, int Objectives, bool IsLocal);

/// <summary>What the results screen shows after a match.</summary>
public sealed class MatchResult
{
    public string Map { get; init; } = "";

    /// <summary>The winning side, or null for no winner.</summary>
    public HudSide? Winner { get; init; }

    public MatchEndReason Reason { get; init; }

    /// <summary>The local player's side, for "You Win" / "You Lose".</summary>
    public HudSide LocalSide { get; init; }

    public int ObjectivesTaken { get; init; }

    public int ObjectivesTotal { get; init; }

    public List<MatchResultRow> Rows { get; init; } = new();

    /// <summary>The request that started the match, for a rematch.</summary>
    public PlayRequest? Request { get; init; }

    public bool LocalWon => Winner is { } w && w == LocalSide;

    public static string ReasonText(MatchEndReason reason) => reason switch
    {
        MatchEndReason.Story => "Objectives completed",
        MatchEndReason.Timeout => "Time is up",
        MatchEndReason.TeamDeath => "No lives left",
        _ => "Match stopped",
    };
}
