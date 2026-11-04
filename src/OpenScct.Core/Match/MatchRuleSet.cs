using System;
using System.Collections.Generic;

namespace OpenScct.Core.Match;

/// <summary>
/// A player-count range (spec/additions/match-size-rules.md): the actor is in the match with <see cref="Min"/> to
/// <see cref="Max"/> players, 0 meaning no limit at that end.
/// </summary>
public readonly record struct PlayerRange(int Min, int Max)
{
    /// <summary>No limit at either end.</summary>
    public static PlayerRange Any => default;

    public bool IsAny => Min == 0 && Max == 0;

    /// <summary>True when a match of <paramref name="players"/> keeps the actor.</summary>
    public bool Keeps(int players) => (Min == 0 || players >= Min) && (Max == 0 || players <= Max);

    /// <summary>"6+ players", "up to 4 players", "6 players", "4-8 players"; "any players" without a limit.</summary>
    public string Short() =>
        IsAny ? "any players"
        : Max == 0 ? $"{Min}+ players"
        : Min == 0 ? $"up to {Max} players"
        : Min == Max ? $"{Min} players"
        : $"{Min}-{Max} players";

    /// <summary>A sentence for status lines and checks.</summary>
    public string Describe() =>
        IsAny ? "in every match"
        : Max == 0 ? $"only in matches with {Min} or more players"
        : Min == 0 ? $"only in matches with up to {Max} players"
        : Min == Max ? $"only in matches with exactly {Min} players"
        : $"only in matches with {Min} to {Max} players";

    /// <summary>Why the range is invalid, or null: each end 0 (any) to <see cref="MatchRuleSet.MostPlayers"/>, min not above max.</summary>
    public string? Problem() =>
        Min < 0 || Max < 0 || Min > MatchRuleSet.MostPlayers || Max > MatchRuleSet.MostPlayers
            ? $"player counts run from 1 to {MatchRuleSet.MostPlayers}, or any"
            : Min > 0 && Max > 0 && Min > Max ? $"the minimum ({Min} players) is more than the maximum ({Max})"
            : null;
}

/// <summary>What a gated actor does on one side of its range.</summary>
public enum HoldKind : byte
{
    /// <summary>Works as built (inside the range only).</summary>
    Normal,

    /// <summary>Left out of the match: hidden, without collision, ignoring events; a player start is not used.</summary>
    Hidden,

    /// <summary>A mover placed at <see cref="Hold.Key"/> before the round and locked there.</summary>
    Key,
}

/// <summary>An actor's state on one side of its range (<c>"normal"</c>, <c>"hidden"</c> or <c>{"key": n}</c>).</summary>
public readonly record struct Hold(HoldKind Kind, int Key)
{
    public static Hold Normal => new(HoldKind.Normal, 0);

    public static Hold Hidden => new(HoldKind.Hidden, 0);

    public static Hold AtKey(int key) => new(HoldKind.Key, key);

    public override string ToString() => Kind switch
    {
        HoldKind.Normal => "normal",
        HoldKind.Hidden => "hidden",
        _ => $"key {Key}",
    };
}

/// <summary>
/// One actor's rule. <see cref="Outside"/> and <see cref="Inside"/> are what the author stored, null for the default:
/// outside its range a mover closes (key 0) and any other actor is left out; inside it works as built.
/// </summary>
public sealed record MatchRule(string Actor, PlayerRange Players, Hold? Outside = null, Hold? Inside = null)
{
    /// <summary>The state outside the range for an actor that is (or is not) a mover.</summary>
    public Hold OutsideFor(bool mover) => Outside ?? (mover ? Hold.AtKey(0) : Hold.Hidden);

    /// <summary>The state inside the range.</summary>
    public Hold InsideFor() => Inside ?? Hold.Normal;
}

/// <summary>A mission's objectives to win by match size: [0] for 1v1 to [11] for 12v12, 0 keeping the map's own number.</summary>
public sealed record WinTargets(string Mission, IReadOnlyList<int> BySize)
{
    /// <summary>The author's number for a match of <paramref name="players"/>, or 0 for the mission's own.</summary>
    public int For(int players)
    {
        int size = MatchRuleSet.TeamSize(players);
        return size - 1 < BySize.Count ? BySize[size - 1] : 0;
    }
}

/// <summary>
/// A map's player-count rules and win targets (SVMap 1.2 <c>sim/match.json</c>, spec/svmap/1.2/match.md). Actor and
/// mission ids match ignoring ASCII case; enumeration keeps document order.
/// </summary>
public sealed class MatchRuleSet
{
    /// <summary>The most players an OpenSCCT match has (twelve a side).</summary>
    public const int MostPlayers = 24;

    /// <summary>Team sizes win targets are set for: 1v1 to 12v12.</summary>
    public const int TeamSizes = 12;

    /// <summary>How deep missions are followed (top mission > zone > objective is 2), as RE+.</summary>
    public const int MaxDepth = 8;

    private readonly Dictionary<string, MatchRule> _rules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WinTargets> _win = new(StringComparer.OrdinalIgnoreCase);

    public MatchRuleSet(IEnumerable<MatchRule> rules, IEnumerable<WinTargets> win)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(win);
        var list = new List<MatchRule>();
        foreach (MatchRule r in rules)
        {
            if (_rules.TryAdd(r.Actor, r))
            {
                list.Add(r);
            }
        }

        var targets = new List<WinTargets>();
        foreach (WinTargets w in win)
        {
            if (_win.TryAdd(w.Mission, w))
            {
                targets.Add(w);
            }
        }

        Rules = list;
        Win = targets;
    }

    public static MatchRuleSet Empty { get; } = new([], []);

    public IReadOnlyList<MatchRule> Rules { get; }

    public IReadOnlyList<WinTargets> Win { get; }

    public bool IsEmpty => Rules.Count == 0 && Win.Count == 0;

    public MatchRule? RuleOf(string actor) => _rules.GetValueOrDefault(actor);

    public WinTargets? TargetsOf(string mission) => _win.GetValueOrDefault(mission);

    /// <summary>True when the actor has a rule that a match of <paramref name="players"/> is outside of.</summary>
    public bool IsOutside(string actor, int players) => players > 0 && RuleOf(actor) is { } r && !r.Players.Keeps(players);

    /// <summary>The match size a match of <paramref name="players"/> plays as: its bigger team, 1v1 to 12v12.</summary>
    public static int TeamSize(int players) => Math.Clamp((players + 1) / 2, 1, TeamSizes);
}
