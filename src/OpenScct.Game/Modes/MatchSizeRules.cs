using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Match;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Modes;

/// <summary>
/// The map's match-size rules at a round start (spec/additions/match-size-rules.md), on the server. It keeps the
/// missions as the map authored them, so every round computes its layout from the same start with that round's
/// player count (<see cref="MatchLayout"/>, shared with the editor), then:
/// <list type="bullet">
/// <item>sets each mission's objectives, MinimumObjectives and RandomObjectives;</item>
/// <item>deactivates the objectives left out, which stay out of play for the round (<see cref="Actor.MatchExcluded"/>);</item>
/// <item>holds gated movers at their key, locked so that no trigger or alarm moves them (<see cref="Mover.MatchLocked"/>);</item>
/// <item>hides other gated actors outside their range (or hidden inside it) and takes their collision away.</item>
/// </list>
/// A level service, made the first time a level with rules starts a match.
/// </summary>
public sealed class MatchSizeRules
{
    private readonly LevelMatchRules _rules;
    private readonly List<(SMission Mission, SObjective?[] Objectives, int Minimum, int Random)> _authored = new();
    private readonly Dictionary<string, SObjective> _objectives = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Actor, (bool Hidden, bool Collide, bool BlockActors, bool BlockPlayers)> _shown =
        new(ReferenceEqualityComparer.Instance);
    private readonly List<Actor> _excluded = new();

    private MatchSizeRules(Level level, LevelMatchRules rules)
    {
        _rules = rules;
        foreach (SMission m in level.AllActors<SMission>())
        {
            _authored.Add((m, m.Objectives.ToArray(), m.MinimumObjectives, m.RandomObjectives));
            _objectives.TryAdd(rules.IdOf(m), m);
            foreach (SObjective? o in m.Objectives)
            {
                if (o is not null)
                {
                    _objectives.TryAdd(rules.IdOf(o), o);
                }
            }
        }
    }

    /// <summary>The player count of the last round start, 0 before the first.</summary>
    public int Players { get; private set; }

    /// <summary>This round's layout, null before the first round start.</summary>
    public MatchOutcome? Outcome { get; private set; }

    /// <summary>The level's rules runner, or null when the map has no rules.</summary>
    public static MatchSizeRules? For(Level level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (level.GetService<MatchSizeRules>() is { } made)
        {
            return made;
        }

        if (level.GetService<LevelMatchRules>() is not { } rules)
        {
            return null;
        }

        var runner = new MatchSizeRules(level, rules);
        level.SetService(runner);
        return runner;
    }

    /// <summary>Lays the round out for <paramref name="players"/> (everyone in the match, bots included).</summary>
    public void Apply(int players)
    {
        Players = players;
        var missions = new List<MatchMission>(_authored.Count);
        foreach ((SMission mission, SObjective?[] objectives, int minimum, int random) in _authored)
        {
            mission.Objectives.Clear();
            mission.Objectives.AddRange(objectives);
            mission.MinimumObjectives = minimum;
            mission.RandomObjectives = random;
            var ids = new string[objectives.Length];
            for (int i = 0; i < objectives.Length; i++)
            {
                ids[i] = objectives[i] is { } o ? _rules.IdOf(o) : string.Empty;
            }

            missions.Add(new MatchMission(_rules.IdOf(mission), ids, minimum, random));
        }

        MatchOutcome outcome = MatchLayout.Apply(missions, _rules.Rules, players);
        Outcome = outcome;
        foreach ((SMission mission, _, _, _) in _authored)
        {
            MatchMission laid = outcome.Mission(_rules.IdOf(mission))!;
            mission.Objectives.Clear();
            foreach (string id in laid.Objectives)
            {
                mission.Objectives.Add(id.Length > 0 ? _objectives.GetValueOrDefault(id) : null);
            }

            mission.MinimumObjectives = laid.MinimumObjectives;
            mission.RandomObjectives = laid.RandomObjectives;
        }

        Restore();
        foreach (string id in outcome.LeftOut)
        {
            if (_objectives.TryGetValue(id, out SObjective? o))
            {
                Exclude(o);
            }
        }

        foreach ((Actor actor, MatchRule rule) in _rules.Gated)
        {
            bool inside = rule.Players.Keeps(players);
            if (actor is Mover mover)
            {
                Hold(mover, inside ? rule.InsideFor() : rule.OutsideFor(mover: true));
            }
            else if (!inside && actor is SObjective objective)
            {
                Exclude(objective);
            }
            else if ((inside ? rule.InsideFor() : rule.OutsideFor(mover: false)).Kind != HoldKind.Normal)
            {
                // Hidden, or a key position on an actor that is not a mover (which the validator rejects).
                Hide(actor);
            }
        }
    }

    /// <summary>
    /// A Disk Hunt mission's disks for this round: the author's target for the match size, as far as its disks reach,
    /// else <paramref name="numDisks"/> (the host's Active Disks), which StartMatch sets just before.
    /// </summary>
    public int DiskHuntDisks(SMission mission, int numDisks)
    {
        ArgumentNullException.ThrowIfNull(mission);
        int target = _rules.Rules.TargetsOf(_rules.IdOf(mission))?.For(Players) ?? 0;
        return target > 0 ? Math.Min(target, mission.Triggers.Count) : numDisks;
    }

    private void Exclude(SObjective objective)
    {
        if (objective.MatchExcluded)
        {
            return;
        }

        objective.MatchExcluded = true;
        _excluded.Add(objective);
        objective.Desactivate();
    }

    private void Hide(Actor actor)
    {
        if (actor.MatchExcluded)
        {
            return;
        }

        _shown.TryAdd(actor, (actor.bHidden, actor.bCollideActors, actor.bBlockActors, actor.bBlockPlayers));
        actor.MatchExcluded = true;
        _excluded.Add(actor);
        actor.bHidden = true;
        actor.SetCollision(false, false, false);
    }

    private void Hold(Mover mover, Hold hold)
    {
        switch (hold.Kind)
        {
            case HoldKind.Hidden:
                Hide(mover);
                break;
            case HoldKind.Key:
                // Out of its trigger behaviour first, so the short leg runs alone; then locked where it stands.
                mover.GotoState(Name.None);
                if (mover.KeyNum != hold.Key)
                {
                    mover.InterpolateTo((byte)Math.Clamp(hold.Key, 0, MoverK.MaxKeys - 1), 0f);
                }

                mover.MatchLocked = true;
                mover.bAllowLock = true;
                mover.Lock();
                break;
        }
    }

    // A new round starts from the authored state: what the last one left out comes back first.
    private void Restore()
    {
        foreach (Actor actor in _excluded)
        {
            actor.MatchExcluded = false;
            if (_shown.Remove(actor, out var shown))
            {
                actor.bHidden = shown.Hidden;
                actor.SetCollision(shown.Collide, shown.BlockActors, shown.BlockPlayers);
            }
        }

        _excluded.Clear();
        foreach ((Actor actor, _) in _rules.Gated)
        {
            if (actor is Mover { MatchLocked: true } mover)
            {
                mover.MatchLocked = false;
                mover.eventUnLock();
            }
        }
    }
}
