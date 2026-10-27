using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;

namespace OpenScct.Bots;

/// <summary>
/// Walks a pawn to a goal over a <see cref="NavGrid"/>: plans, pulls the path straight, and each frame turns the
/// view toward the next waypoint and holds forward. A bot that makes no progress for <see cref="BotK.StuckSeconds"/>
/// plans again from where it stands; after three failed plans the goal counts as unreachable. A bot held up by
/// another pawn gives way (<see cref="GiveWayAfter"/>): it backs away from the pawn for
/// <see cref="BotK.GiveWaySeconds"/>, waits a while that differs from bot to bot and from one time to the next, then
/// plans again from where it stands, so two walkers meeting in a one-lane doorway do not hold each other there.
/// </summary>
public sealed class BotNavigator
{
    private readonly NavGrid _grid;
    private readonly List<FVector> _path = new();
    private int _next;
    private FVector _goal;
    private bool _hasGoal;
    private float _progressAt;
    private float _bestRemaining;
    private int _failures;
    private readonly HashSet<int> _avoid = new();
    private readonly SimRandom _random;
    private float _backUntil = -1f;
    private float _resumeAt = -1f;
    private int _backYaw;

    /// <param name="grid">The walk graph.</param>
    /// <param name="seed">Seeds the waits after giving way (a bot passes its name through <see cref="Seed"/>), so two
    /// bots that gave way to each other do not come back at the same moment, and a run plays the same way again.</param>
    public BotNavigator(NavGrid grid, int seed = 0)
    {
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
        _random = new SimRandom((ulong)(uint)seed, 0, 0);
    }

    /// <summary>A stable seed for <paramref name="name"/> (string hashes differ from one process to the next).</summary>
    public static int Seed(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        uint h = 2166136261;
        foreach (char c in name)
        {
            h = (h ^ c) * 16777619;
        }

        return (int)h;
    }

    /// <summary>
    /// Stuck spells blamed on a pawn in a row after which the walker gives way (1: at the first one). A merc gives way
    /// at once and a spy at the second, so in a doorway the merc backs out first.
    /// </summary>
    public int GiveWayAfter { get; init; } = 1;

    /// <summary>Times the walker gave way to another pawn.</summary>
    public int GiveWays { get; private set; }

    /// <summary>The goal could not be reached (no path, or stuck after replanning).</summary>
    public bool Failed { get; private set; }

    /// <summary>The pawn stands at the goal.</summary>
    public bool Arrived { get; private set; }

    /// <summary>The walker made no progress for <see cref="BotK.StuckSeconds"/> with another pawn in its way.</summary>
    public bool Blocked { get; private set; }

    /// <summary>Stuck spells blamed on a pawn in a row before the place is avoided instead.</summary>
    public const int MaxBlockedStreak = 3;

    private int _blockedStreak;

    /// <summary>Plans that found a path.</summary>
    public int Plans { get; private set; }

    public FVector Goal => _goal;

    /// <summary>The walker's bookkeeping (diagnostics).</summary>
    public string Diagnostics => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"next {_next}/{_path.Count} best {_bestRemaining:F0} since {_progressAt:F2} failures {_failures} avoided {_avoid.Count} blocked {Blocked} gave way {GiveWays}");

    /// <summary>The remaining waypoints.</summary>
    public IReadOnlyList<FVector> Path => _path;

    /// <summary>Sets a new goal (a floor point); the path is planned on the next <see cref="Steer"/>.</summary>
    public void SetGoal(FVector goalFloor)
    {
        _goal = goalFloor;
        _hasGoal = true;
        _path.Clear();
        _next = 0;
        Failed = false;
        Arrived = false;
        Blocked = false;
        _blockedStreak = 0;
        _failures = 0;
        _progressAt = -1f;
        _backUntil = -1f;
        _resumeAt = -1f;
        if (_avoid.Count >= MaxAvoided)
        {
            _avoid.Clear();
        }
    }

    public void Clear()
    {
        _hasGoal = false;
        _path.Clear();
        Arrived = false;
        Failed = false;
        _backUntil = -1f;
        _resumeAt = -1f;
    }

    /// <summary>
    /// The input that walks <paramref name="pawn"/> toward the goal this frame: a view yaw and forward held, or idle
    /// when there is no goal, it is reached or it failed.
    /// </summary>
    public BotInput Steer(Pawn pawn, float now)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        if (!_hasGoal || Failed)
        {
            return BotInput.Idle;
        }

        FVector at = pawn.Location;
        if (Horizontal(_goal - at) < BotK.GoalReach && MathF.Abs(_goal.Z - (at.Z - pawn.CollisionHeight)) < 80f)
        {
            Arrived = true;
            return BotInput.Idle;
        }

        Arrived = false;
        if (_resumeAt >= 0f)
        {
            if (now < _backUntil)
            {
                return new BotInput { BT_Up = 1, View = new FRotator(0, _backYaw, 0), FastSpeed = true };
            }

            if (now < _resumeAt)
            {
                return BotInput.Idle;
            }

            // Back on the way, planned from here (the old path may now start behind a corner).
            _backUntil = -1f;
            _resumeAt = -1f;
            _progressAt = -1f;
            _path.Clear();
            _next = 0;
        }

        if (_path.Count == 0 || _next >= _path.Count)
        {
            if (!Plan(pawn))
            {
                return BotInput.Idle;
            }
        }

        // Progress is the remaining path getting 24 uu shorter than it ever was: a pawn that the server keeps
        // pulling back (its corrections undo what the client walks) makes none.
        bool pawnAhead = _next < _path.Count && PawnAhead(pawn, _path[_next]);
        float remaining = Remaining(at);
        if (_progressAt < 0f)
        {
            _progressAt = now;
            _bestRemaining = remaining;
        }
        else if (remaining < _bestRemaining - 24f)
        {
            _progressAt = now;
            _bestRemaining = remaining;
            Blocked = false;
            _blockedStreak = 0;
        }
        else if (now - _progressAt > BotK.StuckSeconds && pawnAhead && _blockedStreak < MaxBlockedStreak)
        {
            // Held up by another pawn (two walkers in a one-lane passage): the place is fine, so nothing is
            // avoided; the walker keeps pressing and says so (a merc's patrol turns back). After a few of these
            // the place is treated as the problem after all.
            _progressAt = now;
            _bestRemaining = remaining;
            Blocked = true;
            _blockedStreak++;
            if (_blockedStreak >= GiveWayAfter)
            {
                return GiveWay(pawn, now);
            }
        }
        else if (now - _progressAt > BotK.StuckSeconds)
        {
            _progressAt = now;

            // Avoid the place the walker could not get to (a drop or a gap the moves do not take) and plan around it.
            if (_next < _path.Count)
            {
                FVector w = _path[_next];
                Avoid(_grid.NearestNode(w + new FVector(0f, 0f, 1f), 0f));
                Avoid(_grid.NearestNode(new FVector((w.X + at.X) * 0.5f, (w.Y + at.Y) * 0.5f, w.Z + 1f), 0f));
            }

            if (++_failures > 3 || !Plan(pawn))
            {
                Failed = _failures > 3 || Failed;
                return BotInput.Idle;
            }

            _bestRemaining = Remaining(at);
        }

        // A waypoint within reach is passed early only when the next one is in a straight, clear line from here:
        // cutting the corner at a doorway would walk the pawn into its frame. Standing on it, it is passed anyway.
        FVector feet = new(at.X, at.Y, at.Z - pawn.CollisionHeight);
        while (_next < _path.Count - 1 && Horizontal(_path[_next] - at) is var d && d < BotK.WaypointReach &&
            (d < BotK.WaypointArrive || _grid.Clear(feet, _path[_next + 1])))
        {
            _next++;
        }

        FVector target = _path[_next];
        if (_next == _path.Count - 1 && Horizontal(target - at) < BotK.GoalReach)
        {
            target = _goal;
        }

        int yaw = YawTo(at, target);
        var input = new BotInput { BT_Up = 1, View = new FRotator(0, yaw, 0), FastSpeed = true };

        // Two walkers meeting in a passage keep to their right: with a pawn close ahead the walker also strafes
        // right until it is past.
        if (pawnAhead || PawnAhead(pawn, target))
        {
            // Only when there is room on the right: pressed into a wall the pawn would wedge itself.
            FVector heading = target - at;
            float h = Horizontal(heading);
            if (h > 1f)
            {
                var right = new FVector(-heading.Y / h, heading.X / h, 0f);
                if (_grid.Clear(feet, feet + right * BotK.PassingClearance))
                {
                    input.BT_Right = 1;
                }
            }
        }

        return input;
    }

    /// <summary>
    /// Starts giving way: the walker backs away from the pawns close around it (the first clear direction among straight
    /// away from them and up to 90 degrees either side), or only waits when none is clear, then waits
    /// <see cref="BotK.GiveWayWaitMin"/> to <see cref="BotK.GiveWayWaitMax"/> seconds.
    /// </summary>
    private BotInput GiveWay(Pawn pawn, float now)
    {
        GiveWays++;
        FVector at = pawn.Location;
        FVector away = default;
        if (pawn.XLevel is { } level)
        {
            foreach (Pawn other in level.AllActors<Pawn>())
            {
                if (ReferenceEquals(other, pawn) || other.bDeleteMe || !other.bBlockActors || other.Health <= 0)
                {
                    continue;
                }

                FVector d = at - other.Location;
                float distance = Horizontal(d);
                float feetDz = (other.Location.Z - other.CollisionHeight) - (at.Z - pawn.CollisionHeight);
                if (distance >= 1f && distance <= pawn.CollisionRadius + other.CollisionRadius + BotK.PassingClearance &&
                    MathF.Abs(feetDz) <= 40f)
                {
                    away += new FVector(d.X / distance, d.Y / distance, 0f);
                }
            }
        }

        if (Horizontal(away) < 0.1f && _next < _path.Count)
        {
            away = at - _path[_next];
        }

        float wait = BotK.GiveWayWaitMin + _random.FRand() * (BotK.GiveWayWaitMax - BotK.GiveWayWaitMin);
        _backUntil = now;
        _resumeAt = now + wait;
        if (Horizontal(away) < 0.1f)
        {
            return BotInput.Idle;
        }

        var feet = new FVector(at.X, at.Y, at.Z - pawn.CollisionHeight);
        int baseYaw = YawTo(FVector.Zero, away);
        foreach (int turn in GiveWayTurns)
        {
            int yaw = (baseYaw + turn) & 65535;
            var step = new FVector(SvmMath.Cos(yaw) * BotK.GiveWayProbe, SvmMath.Sin(yaw) * BotK.GiveWayProbe, 0f);
            if (_grid.Clear(feet, feet + step))
            {
                _backYaw = yaw;
                _backUntil = now + BotK.GiveWaySeconds;
                _resumeAt = _backUntil + wait;
                return new BotInput { BT_Up = 1, View = new FRotator(0, yaw, 0), FastSpeed = true };
            }
        }

        return BotInput.Idle;
    }

    // Straight away from the pawns first, then 45 and 90 degrees either side (65536 per turn).
    private static readonly int[] GiveWayTurns = [0, 8192, -8192, 16384, -16384];

    /// <summary>Another pawn within <see cref="BotK.PassingClearance"/> of touching, in front of the walker (within 60 degrees of its heading).</summary>
    private static bool PawnAhead(Pawn pawn, FVector target)
    {
        FVector heading = target - pawn.Location;
        float headingLength = Horizontal(heading);
        if (headingLength < 1f || pawn.XLevel is not { } level)
        {
            return false;
        }

        foreach (Pawn other in level.AllActors<Pawn>())
        {
            if (ReferenceEquals(other, pawn) || other.bDeleteMe || !other.bBlockActors || other.Health <= 0)
            {
                continue;
            }

            FVector d = other.Location - pawn.Location;
            float distance = Horizontal(d);
            // Feet within a step of each other: a pawn on another floor is not in the way.
            float feetDz = (other.Location.Z - other.CollisionHeight) - (pawn.Location.Z - pawn.CollisionHeight);
            if (distance > pawn.CollisionRadius + other.CollisionRadius + BotK.PassingClearance || distance < 1f ||
                MathF.Abs(feetDz) > 40f)
            {
                continue;
            }

            float cos = (d.X * heading.X + d.Y * heading.Y) / (distance * headingLength);
            if (cos > 0.5f)
            {
                return true;
            }
        }

        return false;
    }

    // Horizontal length of the rest of the path from the pawn.
    private float Remaining(FVector at)
    {
        if (_next >= _path.Count)
        {
            return Horizontal(_goal - at);
        }

        float length = Horizontal(_path[_next] - at);
        for (int k = _next + 1; k < _path.Count; k++)
        {
            length += Horizontal(_path[k] - _path[k - 1]);
        }

        return length;
    }

    private void Avoid(int node)
    {
        if (node >= 0 && _avoid.Count < MaxAvoided)
        {
            _avoid.Add(node);
        }
    }

    /// <summary>Most nodes one walker avoids (cleared with a new goal).</summary>
    public const int MaxAvoided = 256;

    private bool Plan(Pawn pawn)
    {
        _path.Clear();
        _next = 0;
        int from = _grid.NearestNode(pawn.Location, pawn.CollisionHeight);
        int to = _grid.NearestNode(_goal + new FVector(0f, 0f, 1f), 0f);
        if (!_grid.FindPath(from, to, _path, _avoid))
        {
            // The places avoided so far may cut the walker off: forget them and try once more.
            _avoid.Clear();
            if (!_grid.FindPath(from, to, _path))
            {
                Failed = true;
                return false;
            }
        }

        _grid.Smooth(from >= 0 ? _grid.Nodes[from] : pawn.Location, _path);
        _path.Add(_goal);

        // The smoothed path starts at the nearest node: walk there first when the pawn, off the node, cannot go
        // straight to the first waypoint.
        var feet = new FVector(pawn.Location.X, pawn.Location.Y, pawn.Location.Z - pawn.CollisionHeight);
        if (from >= 0 && !_grid.Clear(feet, _path[0]))
        {
            _path.Insert(0, _grid.Nodes[from]);
        }
        Plans++;
        return true;
    }

    /// <summary>The yaw (65536 per turn) from <paramref name="from"/> toward <paramref name="to"/>.</summary>
    public static int YawTo(FVector from, FVector to)
    {
        FVector d = to - from;
        return new FVector(d.X, d.Y, 0f).Rotation().Yaw;
    }

    public static float Horizontal(FVector d) => MathF.Sqrt(d.X * d.X + d.Y * d.Y);
}
