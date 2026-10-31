using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.SBase;

namespace OpenScct.Game.AI;

/// <summary>
/// A bounded spatial belief search: expand along routes from the last observation, prefer the observed direction
/// of travel, inspect plausible hiding places and share assignments. Never consults the enemy's live state.
/// </summary>
public sealed class AiSearch
{
    private sealed class Site(FVector floor)
    {
        public FVector Floor { get; } = floor;
        public float Evidence;
        public bool Visited;
    }

    private readonly AiWorld _world;
    private readonly object _owner;
    private readonly int _team;
    private readonly List<Site> _sites = [];
    private readonly List<FVector> _visited = [];
    private readonly Dictionary<int, float> _distance = [];
    private readonly PriorityQueue<int, (float Cost, int Node)> _open = new();
    private AiContact? _contact;
    private float _evidenceTime = -100f;
    private FVector _origin;
    private float _retryAfter;
    private float _started;
    private float _radius;
    private float _nextLook;
    private float _nextChoice;
    private int _inspectionsAtStart;
    private Site? _goal;

    public AiSearch(AiWorld world, object owner, int team)
    {
        _world = world;
        _owner = owner;
        _team = team;
    }

    public int Inspections { get; private set; }

    public int Plans { get; private set; }

    public bool Finished { get; private set; }

    /// <summary>An exhausted location is retried only after a pause and a fresh report, or a displaced observation.</summary>
    public bool CanSearch(AiContact contact, float now) => !Finished || !ReferenceEquals(contact, _contact) ||
        contact.Time > _evidenceTime && ((contact.Position - _origin).SizeSquared() > 96f * 96f || now >= _retryAfter);

    public void Reset()
    {
        _contact = null;
        _sites.Clear();
        _visited.Clear();
        _goal = null;
        Finished = false;
        _world.Teams[_team].ReleaseSearch(_owner);
    }

    /// <summary>Selects a reachable place to check, retaining negative evidence until a fresh observation arrives.</summary>
    public bool Next(AiContact contact, SBasePawn observer, SPlayerController pc, AiDifficulty difficulty, float now, out FVector floor)
    {
        bool displaced = contact.Time > _evidenceTime && (contact.Position - _origin).SizeSquared() > 96f * 96f;
        if (!ReferenceEquals(contact, _contact) || displaced || Finished && CanSearch(contact, now))
        {
            Reset();
            _contact = contact;
            _evidenceTime = contact.Time;
            _origin = contact.Position;
            _started = now;
            _nextLook = now + 0.2f;
            _nextChoice = now;
            _inspectionsAtStart = Inspections;
            _radius = SvmMath.Clamp(200f + (now - contact.Time) * AiK.RunSpeed, 250f, 1200f);
            Build(contact.Position);
        }

        floor = default;
        if (Finished)
        {
            return false;
        }

        if (now - _started >= difficulty.SearchSeconds)
        {
            Finish(contact, now);
            return false;
        }

        if (now >= _nextLook)
        {
            _nextLook = now + 0.2f;
            Inspect(observer, pc, difficulty);
        }

        if (_goal is { Visited: false })
        {
            floor = _goal.Floor;
            _world.Teams[_team].ClaimSearch(floor, _owner, now);
            return true;
        }

        if (now < _nextChoice)
        {
            return false;
        }

        Site? best = null;
        float bestScore = -1f;
        bool unclaimed = false;
        FVector predicted = contact.Position + contact.Velocity * SvmMath.Min(1f, SvmMath.Max(0f, now - contact.Time));
        foreach (Site site in _sites)
        {
            if (site.Visited)
            {
                continue;
            }

            float prior = 1f / (1f + (site.Floor - predicted).Size() / 400f);
            float score = prior / (1f + (site.Floor - NavFollower.Feet(observer)).Size() / 500f);
            bool claimed = difficulty.Teamwork > 0.3f && _world.Teams[_team].SearchClaimedByOther(site.Floor, _owner, now);
            unclaimed |= !claimed;
            if (claimed)
            {
                score *= 1f - 0.85f * difficulty.Teamwork;
            }

            if (score > bestScore)
            {
                best = site;
                bestScore = score;
            }
        }

        // If a teammate already covers this entire patch, extend to an exit or another part of the search area.
        if (best is not null && !unclaimed && _radius < 1200f)
        {
            _radius = SvmMath.Min(1200f, _radius + 300f);
            Build(contact.Position);
            return Next(contact, observer, pc, difficulty, now, out floor);
        }

        if (best is null)
        {
            if (_radius < 1200f)
            {
                _radius = SvmMath.Min(1200f, _radius + 300f);
                Build(contact.Position);
                return Next(contact, observer, pc, difficulty, now, out floor);
            }

            Finish(contact, now);
            return false;
        }

        _goal = best;
        _nextChoice = now + 0.4f;
        floor = best.Floor;
        Plans++;
        _world.Teams[_team].ClaimSearch(floor, _owner, now);
        return true;
    }

    /// <summary>Move on after a failed route or prolonged visit, without claiming to have seen through darkness.</summary>
    public void SkipGoal()
    {
        if (_goal is { Visited: false } site)
        {
            Visit(site);
        }
    }

    private void Finish(AiContact contact, float now)
    {
        Finished = true;
        _evidenceTime = contact.Time;
        _retryAfter = now + 3f;
        // Apply once per search, rather than the old 0.3 multiplier every frame after four seconds.
        contact.Confidence *= Inspections > _inspectionsAtStart ? 0.3f : 0.7f;
        _world.Teams[_team].ReleaseSearch(_owner);
    }

    private void Inspect(SBasePawn observer, SPlayerController pc, AiDifficulty difficulty)
    {
        FVector eye = AiSight.Eye(observer);
        AiVisionMode mode = AiSight.ModeOf(pc);
        foreach (Site site in _sites)
        {
            if (site.Visited || mode == AiVisionMode.Emf)
            {
                continue; // EMF cannot rule out an unarmed spy.
            }

            FVector point = site.Floor + new FVector(0f, 0f, AiK.SpyCrouchHeight);
            float distance = (point - eye).Size();
            if (distance > difficulty.SightRange || !AiSight.InView(point - eye, pc.Rotation, pc.FovAngle) ||
                !_world.LineOpen(eye, point) || mode != AiVisionMode.Thermal && _world.SmokeBetween(eye, point))
            {
                continue;
            }

            float light = _world.LightAt(point, AiK.PawnRadius, eye);
            if (mode == AiVisionMode.Night)
            {
                light = (light + AiK.NightBoost) * AiK.NightGain;
            }

            float strength = mode == AiVisionMode.Thermal ? 1f - distance / AiK.ThermalRange
                : AiSight.LitStrength(light, distance, difficulty) * (mode == AiVisionMode.Motion ? AiK.MotionSightFactor : 1f);
            site.Evidence += SvmMath.Max(0f, strength) * 0.2f;
            if (site.Evidence >= difficulty.SpotSeconds)
            {
                Visit(site);
                Inspections++;
            }
        }
    }

    private void Visit(Site site)
    {
        site.Visited = true;
        _visited.Add(site.Floor);
    }

    private void Build(FVector origin)
    {
        NavGraph graph = _world.NavFor(_team == 0);
        IReadOnlySet<int> badLinks = _world.BadLinks(_team == 0);
        int start = graph.NearestNode(origin, 0f, 300f);
        _sites.Clear();
        _goal = null;
        _distance.Clear();
        _open.Clear();
        if (start < 0)
        {
            return;
        }

        _distance[start] = 0f;
        _open.Enqueue(start, (0f, start));
        int expanded = 0;
        while (_open.TryDequeue(out int node, out var priority) && expanded++ < 4096)
        {
            if (priority.Cost > _distance[node])
            {
                continue;
            }

            FVector at = graph.Node(node).Floor;
            bool spaced = true;
            foreach (Site site in _sites)
            {
                spaced &= (site.Floor - at).SizeSquared() >= 180f * 180f;
            }

            foreach (FVector old in _visited)
            {
                spaced &= (old - at).SizeSquared() >= 150f * 150f;
            }

            if (spaced && _sites.Count < 32)
            {
                _sites.Add(new Site(at));
            }

            foreach (var edge in graph.EdgesOf(node))
            {
                float cost = priority.Cost + edge.Cost;
                if (cost <= _radius && !badLinks.Contains(edge.Link) &&
                    (!_distance.TryGetValue(edge.To, out float old) || cost < old))
                {
                    _distance[edge.To] = cost;
                    _open.Enqueue(edge.To, (cost, edge.To));
                }
            }
        }
    }
}
