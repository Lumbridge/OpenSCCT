using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Match;
using OpenScct.Game.SBase;

namespace OpenScct.Game.Modes;

/// <summary>
/// The map's match-size rules at a round start (spec/additions/match-size-rules.md). On the server it keeps the
/// missions as the map authored them, so every round computes its layout from the same start with that round's
/// player count (<see cref="MatchLayout"/>, shared with the editor), then:
/// <list type="bullet">
/// <item>sets each mission's objectives, MinimumObjectives and RandomObjectives;</item>
/// <item>deactivates the objectives left out, which stay out of play for the round (<see cref="Actor.MatchExcluded"/>);</item>
/// <item>holds gated movers at their key, locked so that no trigger or alarm moves them (<see cref="Mover.MatchLocked"/>);</item>
/// <item>leaves other gated actors out (and "hidden" ones inside their range): hidden, without collision, deaf to
/// events, not ticking and, for a player start, never chosen.</item>
/// </list>
/// A client lays out its own missions and leaves the same actors out from the replicated
/// <see cref="GameReplicationInfo.MatchPlayers"/> (<see cref="ApplyOnClient"/>): mission lists do not replicate, a
/// hidden actor's flags reach a client only in its first bunch, actors the client loads itself never hear from the
/// server at all, and ticking is each world's own. A level service, made the first time it is needed.
/// </summary>
public sealed class MatchSizeRules
{
    private readonly LevelMatchRules _rules;
    private readonly List<(SMission Mission, SObjective?[] Objectives, int Minimum, int Random)> _authored = new();
    private readonly Dictionary<string, SObjective> _objectives = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Actor, (bool Hidden, bool Collide, bool BlockActors, bool BlockPlayers)> _shown =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PlayerStart, bool> _starts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Mover, bool> _allowLock = new(ReferenceEqualityComparer.Instance);
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

    /// <summary>The player count of the last layout, 0 before the first.</summary>
    public int Players { get; private set; }

    /// <summary>This round's layout on the server, null before the first round start.</summary>
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

    /// <summary>Lays the round out for <paramref name="players"/> (everyone in the match, bots included), on the server.</summary>
    public void Apply(int players)
    {
        Players = players;
        MatchOutcome outcome = Layout(players);
        Outcome = outcome;
        LayMissions(outcome);
        Restore();
        LeaveOut(outcome, players, server: true);
    }

    /// <summary>
    /// A client's side of the layout for <paramref name="players"/> (the replicated count; 0 before the server has laid
    /// a round out): the missions list the same objectives and targets as on the server (the mission status page reads
    /// them), the same actors are left out, so they stop ticking and hearing events, and the hidden ones, movers held
    /// hidden among them, lose their look and collision. Objectives come through replication already deactivated, and
    /// movers held at a key already moving to it.
    /// </summary>
    public void ApplyOnClient(int players)
    {
        Players = players;
        MatchOutcome? outcome = players > 0 ? Layout(players) : null;
        LayMissions(outcome);
        Restore();
        if (outcome is not null)
        {
            LeaveOut(outcome, players, server: false);
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

    // The layout of the missions as the map authored them, for this many players.
    private MatchOutcome Layout(int players)
    {
        var missions = new List<MatchMission>(_authored.Count);
        foreach ((SMission mission, SObjective?[] objectives, int minimum, int random) in _authored)
        {
            var ids = new string[objectives.Length];
            for (int i = 0; i < objectives.Length; i++)
            {
                ids[i] = objectives[i] is { } o ? _rules.IdOf(o) : string.Empty;
            }

            missions.Add(new MatchMission(_rules.IdOf(mission), ids, minimum, random));
        }

        return MatchLayout.Apply(missions, _rules.Rules, players);
    }

    // Each mission's objectives, MinimumObjectives and RandomObjectives as laid out, or as authored without a layout.
    private void LayMissions(MatchOutcome? outcome)
    {
        foreach ((SMission mission, SObjective?[] objectives, int minimum, int random) in _authored)
        {
            mission.Objectives.Clear();
            if (outcome?.Mission(_rules.IdOf(mission)) is not { } laid)
            {
                mission.Objectives.AddRange(objectives);
                mission.MinimumObjectives = minimum;
                mission.RandomObjectives = random;
                continue;
            }

            foreach (string id in laid.Objectives)
            {
                mission.Objectives.Add(id.Length > 0 ? _objectives.GetValueOrDefault(id) : null);
            }

            mission.MinimumObjectives = laid.MinimumObjectives;
            mission.RandomObjectives = laid.RandomObjectives;
        }
    }

    // Leaves the round's objectives and gated actors out. Only the server deactivates objectives and moves movers:
    // clients get both through replication.
    private void LeaveOut(MatchOutcome outcome, int players, bool server)
    {
        foreach (string id in outcome.LeftOut)
        {
            if (_objectives.TryGetValue(id, out SObjective? o))
            {
                Exclude(o, server);
            }
        }

        foreach ((Actor actor, MatchRule rule) in _rules.Gated)
        {
            bool inside = rule.Players.Keeps(players);
            if (actor is Mover mover)
            {
                Hold hold = inside ? rule.InsideFor() : rule.OutsideFor(mover: true);
                if (server)
                {
                    Hold(mover, hold);
                }
                else if (hold.Kind == HoldKind.Hidden)
                {
                    Hide(mover);
                }
            }
            else if (!inside && actor is SObjective objective)
            {
                Exclude(objective, server);
            }
            else if (IsHidden(rule, inside))
            {
                Hide(actor);
            }
        }
    }

    // An actor that is neither a mover nor an objective outside its range: "hidden", or a key position on an actor
    // that is not a mover (which the validator rejects), takes it out.
    private static bool IsHidden(MatchRule rule, bool inside) =>
        (inside ? rule.InsideFor() : rule.OutsideFor(mover: false)).Kind != HoldKind.Normal;

    private void Exclude(SObjective objective, bool deactivate)
    {
        if (objective.MatchExcluded)
        {
            return;
        }

        objective.MatchExcluded = true;
        _excluded.Add(objective);
        if (deactivate)
        {
            objective.Desactivate();
        }
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
        if (actor is PlayerStart start)
        {
            // No one spawns at a start the round leaves out.
            _starts.TryAdd(start, start.bEnabled);
            start.bEnabled = false;
        }
    }

    private void Hold(Mover mover, Hold hold)
    {
        switch (hold.Kind)
        {
            case HoldKind.Hidden:
                // Locked as well, so that nothing moves it while it is out of the round.
                Lock(mover);
                Hide(mover);
                break;
            case HoldKind.Key:
                // Out of its trigger behaviour first, so the short leg runs alone; then locked where it stands.
                mover.GotoState(Name.None);
                byte key = (byte)Math.Clamp(hold.Key, 0, MoverK.MaxKeys - 1);
                if (mover.bInterpolating || mover.KeyNum != key)
                {
                    // A leg that starts "from" the key it goes to: KeyFrameReached carries a leg on through the keys
                    // in its direction, and a leg with no direction stops at its key. A client starts the same leg
                    // from SimInterpolate, which carries both keys, so it stops there too.
                    mover.KeyNum = key;
                    mover.PrevKeyNum = key;
                    mover.InterpolateTo(key, 0f);
                }

                Lock(mover);
                break;
        }
    }

    private void Lock(Mover mover)
    {
        _allowLock.TryAdd(mover, mover.bAllowLock);
        mover.MatchLocked = true;
        mover.bAllowLock = true;
        mover.Lock();
    }

    // A new layout starts from the authored state: what the last one left out comes back first.
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
        foreach ((PlayerStart start, bool enabled) in _starts)
        {
            start.bEnabled = enabled;
        }

        _starts.Clear();
        foreach ((Mover mover, bool allowLock) in _allowLock)
        {
            mover.MatchLocked = false;
            mover.eventUnLock();
            mover.bAllowLock = allowLock;
        }

        _allowLock.Clear();
    }
}
