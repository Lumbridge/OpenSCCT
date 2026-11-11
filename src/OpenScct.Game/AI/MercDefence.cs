using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;

namespace OpenScct.Game.AI;

/// <summary>
/// How the merc bots divide the objectives that are left, as a team of players does: each defender gets a group of
/// objectives that lie together (his sector), which he mines and patrols. A human merc keeps the objective he stays
/// near and the bots share out the rest; with more defenders than groups the extra ones roam over all of them. A bot
/// keeps its sector across refreshes unless the objectives or the team change. Only public facts are read: the
/// objectives the HUD lists and where the team mates are (the merc radar shows them).
/// </summary>
public sealed class MercDefence
{
    /// <summary>Seconds between two looks at where the team stands.</summary>
    public const float RefreshSeconds = 10f;

    /// <summary>A human merc this close to an objective is taken to hold it (uu).</summary>
    public const float HumanHoldRange = 1500f;

    private readonly AiWorld _world;
    private readonly Dictionary<SBasePawn, List<SObjectiveTrigger>> _sectors = new(ReferenceEqualityComparer.Instance);
    private readonly List<SObjectiveTrigger> _objectives = new();
    private readonly List<SBasePawn> _defenders = new();
    private readonly List<SObjectiveTrigger> _lastObjectives = new();
    private readonly List<SBasePawn> _lastDefenders = new();
    private float _refreshedAt = -100f;
    private float _gatheredAt = -100f;

    public MercDefence(AiWorld world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>Times the sectors were worked out (diagnostics).</summary>
    public int Refreshes { get; private set; }

    /// <summary>An objective a merc can still defend: a terminal not taken, an active bomb site, a disk on the map.</summary>
    public static bool Defensible(AiWorld world, SObjectiveTrigger objective)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(objective);
        return !objective.bDeleteMe && !objective.complete && objective.Objective?.ObjectiveTaken != true &&
            (objective is SComputerObjectiveTrigger terminal ? !world.IsTaken(terminal) :
                objective.bActive && (objective is SBombTargetObjectiveTrigger || objective is SDisk { bHidden: false, bPickable: true } ||
                    objective is SFlag flag && !Carried(world, flag) || objective is SFlagDropZone && Carried(world, null)));
    }

    /// <summary>
    /// A spy carries <paramref name="flag"/> (any disk when null), as the HUD tells everyone. A disk at home is
    /// defended; once one is taken the drop zones are what is left to hold.
    /// </summary>
    private static bool Carried(AiWorld world, SFlag? flag)
    {
        foreach (SPawnAttaque spy in world.Level.DynamicActors<SPawnAttaque>())
        {
            if (!spy.bDeleteMe && spy.Health > 0 && spy.CarriedFlag is { } carried && (flag is null || ReferenceEquals(carried, flag)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The objectives <paramref name="merc"/> defends (empty when none is left or he is not on the team).</summary>
    public IReadOnlyList<SObjectiveTrigger> Sector(SBasePawn merc, float now)
    {
        ArgumentNullException.ThrowIfNull(merc);
        if (now - _gatheredAt >= 0.5f || now < _gatheredAt)
        {
            _gatheredAt = now;
            Gather();
            if (now - _refreshedAt >= RefreshSeconds || now < _refreshedAt || !Same(_objectives, _lastObjectives) ||
                !Same(_defenders, _lastDefenders))
            {
                Refresh(now);
            }
        }

        return _sectors.TryGetValue(merc, out List<SObjectiveTrigger>? sector) ? sector : [];
    }

    private void Gather()
    {
        _objectives.Clear();
        foreach (SObjectiveTrigger t in _world.Level.AllActors<SObjectiveTrigger>())
        {
            if (Defensible(_world, t))
            {
                _objectives.Add(t);
            }
        }

        _defenders.Clear();
        foreach (SPAWNDEFENSE m in _world.Level.DynamicActors<SPAWNDEFENSE>())
        {
            if (!m.bDeleteMe && m.Health > 0)
            {
                _defenders.Add(m);
            }
        }
    }

    private static bool Same<T>(List<T> a, List<T> b) where T : class
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!ReferenceEquals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBot(SBasePawn pawn) => pawn.Controller is PlayerController { Player: AiViewport };

    private void Refresh(float now)
    {
        _refreshedAt = now;
        Refreshes++;
        _lastObjectives.Clear();
        _lastObjectives.AddRange(_objectives);
        _lastDefenders.Clear();
        _lastDefenders.AddRange(_defenders);

        var previous = new Dictionary<SBasePawn, List<SObjectiveTrigger>>(_sectors, ReferenceEqualityComparer.Instance);
        _sectors.Clear();
        if (_objectives.Count == 0)
        {
            return;
        }

        // A human who stays near an objective holds it; the bots share out the others.
        var open = new List<SObjectiveTrigger>(_objectives);
        var bots = new List<SBasePawn>();
        foreach (SBasePawn defender in _defenders)
        {
            if (IsBot(defender))
            {
                bots.Add(defender);
                continue;
            }

            SObjectiveTrigger? held = null;
            float heldD = HumanHoldRange;
            foreach (SObjectiveTrigger t in _objectives)
            {
                float d = (t.Location - defender.Location).Size();
                if (d < heldD)
                {
                    heldD = d;
                    held = t;
                }
            }

            if (held is not null)
            {
                open.Remove(held);
            }
        }

        if (bots.Count == 0)
        {
            return;
        }

        // With every objective held by a human the bots back them up everywhere.
        if (open.Count == 0)
        {
            open.AddRange(_objectives);
        }

        List<List<SObjectiveTrigger>> groups = Groups(open, Math.Min(bots.Count, open.Count));

        // Each group to one bot, nearest pairs first; a bot is half as far from a group it already defends.
        var free = new List<SBasePawn>(bots);
        var unassigned = new List<List<SObjectiveTrigger>>(groups);
        while (unassigned.Count > 0 && free.Count > 0)
        {
            int bestBot = 0;
            int bestGroup = 0;
            float best = float.MaxValue;
            for (int b = 0; b < free.Count; b++)
            {
                previous.TryGetValue(free[b], out List<SObjectiveTrigger>? had);
                for (int g = 0; g < unassigned.Count; g++)
                {
                    float cost = (Centre(unassigned[g]) - free[b].Location).Size();
                    if (had is not null && Shares(had, unassigned[g]))
                    {
                        cost *= 0.5f;
                    }

                    if (cost < best)
                    {
                        best = cost;
                        bestBot = b;
                        bestGroup = g;
                    }
                }
            }

            _sectors[free[bestBot]] = unassigned[bestGroup];
            free.RemoveAt(bestBot);
            unassigned.RemoveAt(bestGroup);
        }

        // More bots than groups: the others roam over every open objective.
        foreach (SBasePawn roamer in free)
        {
            _sectors[roamer] = new List<SObjectiveTrigger>(open);
        }
    }

    /// <summary>
    /// <paramref name="count"/> groups of objectives that lie together: seeds chosen farthest apart (the first in actor
    /// order), every objective with its nearest seed.
    /// </summary>
    private static List<List<SObjectiveTrigger>> Groups(List<SObjectiveTrigger> objectives, int count)
    {
        var seeds = new List<SObjectiveTrigger> { objectives[0] };
        while (seeds.Count < count)
        {
            SObjectiveTrigger? far = null;
            float farD = -1f;
            foreach (SObjectiveTrigger t in objectives)
            {
                float nearest = float.MaxValue;
                foreach (SObjectiveTrigger s in seeds)
                {
                    nearest = MathF.Min(nearest, (t.Location - s.Location).SizeSquared());
                }

                if (nearest > farD)
                {
                    farD = nearest;
                    far = t;
                }
            }

            seeds.Add(far!);
        }

        var groups = new List<List<SObjectiveTrigger>>(count);
        for (int i = 0; i < count; i++)
        {
            groups.Add(new List<SObjectiveTrigger>());
        }

        foreach (SObjectiveTrigger t in objectives)
        {
            int nearest = 0;
            float nearestD = float.MaxValue;
            for (int i = 0; i < seeds.Count; i++)
            {
                float d = (t.Location - seeds[i].Location).SizeSquared();
                if (d < nearestD)
                {
                    nearestD = d;
                    nearest = i;
                }
            }

            groups[nearest].Add(t);
        }

        return groups;
    }

    private static FVector Centre(List<SObjectiveTrigger> group)
    {
        FVector sum = default;
        foreach (SObjectiveTrigger t in group)
        {
            sum += t.Location;
        }

        return sum * (1f / group.Count);
    }

    private static bool Shares(List<SObjectiveTrigger> a, List<SObjectiveTrigger> b)
    {
        foreach (SObjectiveTrigger t in a)
        {
            if (b.Contains(t))
            {
                return true;
            }
        }

        return false;
    }
}
