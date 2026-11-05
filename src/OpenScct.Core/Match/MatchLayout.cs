using System;
using System.Collections.Generic;

namespace OpenScct.Core.Match;

/// <summary>A mission as the map authored it: its objectives (missions among them) and its targets.</summary>
public sealed record MatchMission(string Id, IReadOnlyList<string> Objectives, int MinimumObjectives, int RandomObjectives = 0);

/// <summary>What a match of <see cref="Players"/> plays (<see cref="MatchLayout.Apply"/>).</summary>
public sealed class MatchOutcome
{
    private readonly Dictionary<string, MatchMission> _missions;
    private readonly HashSet<string> _left;

    internal MatchOutcome(int players, List<MatchMission> missions, List<string> leftOut)
    {
        Players = players;
        MissionList = missions;
        _missions = new Dictionary<string, MatchMission>(StringComparer.OrdinalIgnoreCase);
        foreach (MatchMission m in missions)
        {
            _missions.TryAdd(m.Id, m);
        }

        LeftOut = leftOut;
        _left = new HashSet<string>(leftOut, StringComparer.OrdinalIgnoreCase);
    }

    public int Players { get; }

    /// <summary>Every mission after pruning, in the authored order (lists, MinimumObjectives and RandomObjectives set).</summary>
    public IReadOnlyList<MatchMission> MissionList { get; }

    /// <summary>The objectives and missions left out, in the order they were dropped.</summary>
    public IReadOnlyList<string> LeftOut { get; }

    public MatchMission? Mission(string id) => _missions.GetValueOrDefault(id);

    public bool IsLeftOut(string id) => _left.Contains(id);
}

/// <summary>A summary line for one player count: "6 players (3v3): 5 objectives, the spies need 3".</summary>
public sealed record MatchSummary(int Players, int Objectives, int Needed, int RandomOf)
{
    public string Text
    {
        get
        {
            int size = MatchRuleSet.TeamSize(Players);
            string count = RandomOf > 0 ? $"{Objectives} objectives, {RandomOf} picked at random" : $"{Objectives} objective{(Objectives == 1 ? "" : "s")}";
            return $"{Players} player{(Players == 1 ? "" : "s")} ({size}v{size}): {count}, the spies need {Needed}";
        }
    }
}

/// <summary>
/// The match-size rules applied to a map's missions (spec/additions/match-size-rules.md), shared by the server, the
/// editor's preview and Map Check so they always agree. It is RE+'s SMatchSizeRules prune, step for step, widened to
/// 24 players and 12v12: an objective outside its range comes out of its mission; a mission left with nothing comes
/// out of its parent; each mission then asks for as many fewer completions as it lost, never more than it can still
/// give and never below 1 while something remains; an author's target for the match size replaces that, as far as
/// the objectives left can reach; RandomObjectives is clamped to what is left. Top missions are never left out.
/// It works on a copy of the authored missions, so every round computes from the same start.
/// </summary>
public static class MatchLayout
{
    /// <summary>The missions no other mission lists: each game mode's own.</summary>
    public static List<string> Roots(IReadOnlyList<MatchMission> missions)
    {
        ArgumentNullException.ThrowIfNull(missions);
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MatchMission m in missions)
        {
            foreach (string child in m.Objectives)
            {
                if (!string.Equals(child, m.Id, StringComparison.OrdinalIgnoreCase))
                {
                    listed.Add(child);
                }
            }
        }

        var roots = new List<string>();
        foreach (MatchMission m in missions)
        {
            if (!listed.Contains(m.Id))
            {
                roots.Add(m.Id);
            }
        }

        return roots;
    }

    /// <summary>What a match of <paramref name="players"/> plays; with no players (or no rules) everything stays.</summary>
    public static MatchOutcome Apply(IReadOnlyList<MatchMission> missions, MatchRuleSet rules, int players)
    {
        ArgumentNullException.ThrowIfNull(missions);
        ArgumentNullException.ThrowIfNull(rules);
        var state = new Pruning(missions, rules, players);
        if (players > 0)
        {
            foreach (string root in Roots(missions))
            {
                state.Prune(root, 0);
            }
        }

        return state.Outcome();
    }

    /// <summary>The objectives a match of <paramref name="players"/> plays and how many completions the spies need.</summary>
    public static MatchSummary Summarize(IReadOnlyList<MatchMission> missions, MatchRuleSet rules, int players)
    {
        MatchOutcome outcome = Apply(missions, rules, players);
        int objectives = 0, needed = 0, random = 0;
        foreach (string root in Roots(missions))
        {
            MatchMission top = outcome.Mission(root)!;
            needed += Needs(top);
            if (top.RandomObjectives > 0 && top.RandomObjectives < top.Objectives.Count)
            {
                random += top.RandomObjectives;
            }

            var stack = new List<string>(top.Objectives);
            for (int guard = 0; stack.Count > 0 && guard < 4096; guard++)
            {
                string child = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                if (outcome.Mission(child) is { } sub)
                {
                    stack.AddRange(sub.Objectives);
                }
                else
                {
                    objectives++;
                }
            }
        }

        return new MatchSummary(players, objectives, needed, random);
    }

    /// <summary>
    /// Player counts where a mission's target asks for more than the match can give: "With 5 players (3v3), Main asks
    /// for 5 objectives to win but only 4 can be done there; it will ask for 4."
    /// </summary>
    public static List<string> TargetWarnings(IReadOnlyList<MatchMission> missions, MatchRuleSet rules, Func<string, string>? name = null)
    {
        var warnings = new List<string>();
        foreach (WinTargets w in rules.Win)
        {
            for (int size = 1; size <= MatchRuleSet.TeamSizes && size <= w.BySize.Count; size++)
            {
                int wanted = w.BySize[size - 1];
                if (wanted <= 0)
                {
                    continue;
                }

                // Both counts of the size (5 and 6 players play 3v3) can leave out different objectives.
                foreach (int players in new[] { (size * 2) - 1, size * 2 })
                {
                    MatchMission? found = Apply(missions, rules, players).Mission(w.Mission);
                    if (found is null || found.Objectives.Count == 0 || found.MinimumObjectives >= wanted)
                    {
                        continue;
                    }

                    warnings.Add($"With {players} player{(players == 1 ? "" : "s")} ({size}v{size}), {name?.Invoke(w.Mission) ?? w.Mission} asks for " +
                                 $"{wanted} objectives to win but only {found.MinimumObjectives} can be done there; it will ask for {found.MinimumObjectives}.");
                }
            }
        }

        return warnings;
    }

    /// <summary>Runs of player counts where a top mission has nothing left to play, one line per run.</summary>
    public static List<string> Problems(IReadOnlyList<MatchMission> missions, MatchRuleSet rules, Func<string, string>? name = null)
    {
        var problems = new List<string>();
        var authored = new Dictionary<string, MatchMission>(StringComparer.OrdinalIgnoreCase);
        foreach (MatchMission m in missions)
        {
            authored.TryAdd(m.Id, m);
        }

        foreach (string root in Roots(missions))
        {
            if (authored[root].Objectives.Count == 0)
            {
                continue;
            }

            int first = 0;
            for (int n = 1; n <= MatchRuleSet.MostPlayers + 1; n++)
            {
                bool empty = n <= MatchRuleSet.MostPlayers && Apply(missions, rules, n).Mission(root)!.Objectives.Count == 0;
                if (empty && first == 0)
                {
                    first = n;
                }
                else if (!empty && first != 0)
                {
                    int last = n - 1;
                    string counts = first == last ? $"{first} player{(first == 1 ? "" : "s")}" : $"{first} to {last} players";
                    problems.Add($"With {counts}, {name?.Invoke(root) ?? root} has no objectives left: the player rules leave nothing to play.");
                    first = 0;
                }
            }
        }

        return problems;
    }

    /// <summary>The completions a mission asks for: MinimumObjectives, else every objective.</summary>
    public static int Needs(MatchMission m) => m.MinimumObjectives > 0 ? m.MinimumObjectives : m.Objectives.Count;

    /// <summary>The working copy one <see cref="Apply"/> prunes.</summary>
    private sealed class Pruning
    {
        private readonly List<string> _order = new();
        private readonly Dictionary<string, (List<string> Objectives, int Minimum, int Random)> _missions =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _left = new();
        private readonly HashSet<string> _pruning = new(StringComparer.OrdinalIgnoreCase);
        private readonly MatchRuleSet _rules;
        private readonly int _players;

        public Pruning(IReadOnlyList<MatchMission> missions, MatchRuleSet rules, int players)
        {
            _rules = rules;
            _players = players;
            foreach (MatchMission m in missions)
            {
                if (_missions.TryAdd(m.Id, (new List<string>(m.Objectives), m.MinimumObjectives, m.RandomObjectives)))
                {
                    _order.Add(m.Id);
                }
            }
        }

        public void Prune(string id, int depth)
        {
            _pruning.Add(id);
            List<string> objectives = _missions[id].Objectives;
            int fewer = 0, capacity = 0;
            for (int i = objectives.Count - 1; i >= 0; i--)
            {
                string child = objectives[i];
                if (child.Length == 0)
                {
                    continue;
                }

                bool mission = _missions.ContainsKey(child);
                if (_rules.IsOutside(child, _players))
                {
                    if (mission)
                    {
                        fewer += Needs(child);
                    }

                    objectives.RemoveAt(i);
                    Drop(child, depth);
                    continue;
                }

                // A mission that lists itself or one above it (a loop) counts as one entry here: pruning it again
                // would change the list this loop walks.
                if (!mission || depth >= MatchRuleSet.MaxDepth || _pruning.Contains(child))
                {
                    capacity++;
                    continue;
                }

                int before = Needs(child);
                Prune(child, depth + 1);
                if (_missions[child].Objectives.Count == 0)
                {
                    fewer += before;
                    objectives.RemoveAt(i);
                    Drop(child, depth);
                    continue;
                }

                fewer += before - Needs(child);
                capacity += Needs(child);
            }

            (List<string> list, int minimum, int random) = _missions[id];
            if (minimum > 0)
            {
                minimum = Math.Min(minimum - fewer, capacity);
                if (minimum < 1 && list.Count > 0)
                {
                    minimum = 1;
                }

                minimum = Math.Max(minimum, 0);
            }

            int target = _rules.TargetsOf(id)?.For(_players) ?? 0;
            if (target > 0)
            {
                minimum = Math.Min(target, capacity);
                if (minimum < 1 && list.Count > 0)
                {
                    minimum = 1;
                }
            }

            _missions[id] = (list, minimum, Math.Min(random, list.Count));
            _pruning.Remove(id);
        }

        // The objective (and for a mission, everything it still lists) is out of the match.
        private void Drop(string id, int depth)
        {
            _left.Add(id);
            if (depth < MatchRuleSet.MaxDepth && _missions.TryGetValue(id, out var sub))
            {
                foreach (string child in sub.Objectives)
                {
                    if (child.Length > 0)
                    {
                        Drop(child, depth + 1);
                    }
                }
            }
        }

        private int Needs(string id)
        {
            (List<string> list, int minimum, _) = _missions[id];
            return minimum > 0 ? minimum : list.Count;
        }

        public MatchOutcome Outcome()
        {
            var missions = new List<MatchMission>(_order.Count);
            foreach (string id in _order)
            {
                (List<string> list, int minimum, int random) = _missions[id];
                missions.Add(new MatchMission(id, list, minimum, random));
            }

            // An objective two missions list, or a loop, is dropped more than once; it is reported once.
            var left = new List<string>(_left.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in _left)
            {
                if (seen.Add(id))
                {
                    left.Add(id);
                }
            }

            return new MatchOutcome(_players, missions, left);
        }
    }
}
