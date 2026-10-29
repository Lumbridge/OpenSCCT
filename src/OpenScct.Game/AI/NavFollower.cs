using System;
using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.AI;

/// <summary>What the follower wants this tick: the yaw to face, the keys, and the stance.</summary>
public struct NavCommand
{
    /// <summary>The yaw (65536 a turn) the view should turn to, or null to leave it.</summary>
    public int? Yaw;

    /// <summary>The largest yaw error (65536 a turn) at which the keys still apply; beyond it the bot turns first.</summary>
    public int Tolerance;

    public AiInput Keys;

    /// <summary>The stance wanted (true crouched), or null for the caller's choice.</summary>
    public bool? Crouched;

    /// <summary>True while a traversal move is running (the brain should not take the input over).</summary>
    public bool Busy;
}

/// <summary>
/// Walks a pawn along a <see cref="NavGraph"/> path with the keys a player would press: it faces the next waypoint and
/// holds forward, passes waypoints early when the next is in a clear straight line, crouches for crawl edges, walks
/// off ledges for drops, and plays each traversal link as the recorded moves do (docs/spikes/R4-traversal.md): run at
/// the wall, stop, jump and hold forward for a ledge; walk into a pipe or a ladder and hold forward to climb it (back
/// to climb a ladder down from its top); run into a fence, climb, cross the top and climb down; walk in under a
/// horizontal pipe, jump, go hand over hand and drop at the far end; run, jump under a zip line and ride. A walker that makes no progress plans again around the place; a link that fails is left out of
/// the side's plans for a while, and so are the edges across a place where a walker stuck twice (a closed door).
/// </summary>
public sealed class NavFollower
{
    private const ETraceFlags WorldFlags = ETraceFlags.TRACE_Blocking | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry |
        ETraceFlags.TRACE_Volumes | ETraceFlags.TRACE_Others;

    /// <summary>What shuts a way at run time as well: the world and movers (doors).</summary>
    private const ETraceFlags BarrierFlags = WorldFlags | ETraceFlags.TRACE_Movers;

    private readonly AiWorld _world;
    private readonly bool _spy;
    private readonly List<NavStep> _path = new();
    private readonly HashSet<int> _avoid = new();
    private int _next;
    private FVector _goal;
    private bool _hasGoal;
    private float _stealth;
    private Func<int, float>? _penalty;
    private float _progressAt = -1f;
    private float _bestRemaining;
    private int _replans;

    // Traversal state.
    private enum TPhase : byte
    {
        None,
        Approach,
        Face,
        Press,
        Settle,
        RunUp,
        Air,
        Hang,
        Climb,
        Ride,
        Bar,
    }

    private TPhase _phase;
    private float _phaseAt;
    private int _tries;
    private float _slowSince = -1f;

    public NavFollower(AiWorld world, bool spy)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _spy = spy;
    }

    public NavGraph Graph => _world.NavFor(_spy);

    public bool HasGoal => _hasGoal;

    public bool Arrived { get; private set; }

    /// <summary>The goal could not be reached (no path, or stuck after replanning).</summary>
    public bool Failed { get; private set; }

    public FVector Goal => _goal;

    public IReadOnlyList<NavStep> Path => _path;

    public int NextIndex => _next;

    /// <summary>Traversal links completed and failed (diagnostics).</summary>
    public int LinksDone { get; private set; }

    public int LinksFailed { get; private set; }

    public int Plans { get; private set; }

    /// <summary>The traversal link running, or -1.</summary>
    public int ActiveLink => _phase != TPhase.None && _next < _path.Count ? _path[_next].Link : -1;

    /// <summary>The kind of the step being walked.</summary>
    public NavEdgeKind? CurrentKind => _next < _path.Count ? _path[_next].Kind : null;

    /// <summary>Path length still to go (horizontal), from the pawn.</summary>
    public float Remaining(FVector at)
    {
        if (_next >= _path.Count)
        {
            return Horizontal(_goal - at);
        }

        float length = Horizontal(_path[_next].Point - at);
        for (int k = _next + 1; k < _path.Count; k++)
        {
            length += Horizontal(_path[k].Point - _path[k - 1].Point);
        }

        return length;
    }

    /// <summary>
    /// Plans to <paramref name="goalFloor"/> from the pawn now. <paramref name="stealth"/> weighs light on the route,
    /// <paramref name="penalty"/> adds a cost per node (danger). False when it cannot be reached.
    /// </summary>
    public bool SetGoal(Pawn pawn, FVector goalFloor, float stealth = 0f, Func<int, float>? penalty = null)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        _goal = goalFloor;
        _hasGoal = true;
        _stealth = stealth;
        _penalty = penalty;
        _avoid.Clear();
        _replans = 0;
        Arrived = false;
        Failed = false;
        return Plan(pawn);
    }

    public void Clear()
    {
        _hasGoal = false;
        _path.Clear();
        _next = 0;
        _phase = TPhase.None;
        Arrived = false;
        Failed = false;
    }

    private bool Plan(Pawn pawn)
    {
        _path.Clear();
        _next = 0;
        _phase = TPhase.None;
        _progressAt = -1f;
        NavGraph g = Graph;
        int from = g.NearestNode(pawn.Location, pawn.CollisionHeight);
        int to = g.NearestNode(_goal + new FVector(0f, 0f, 1f));
        Func<int, float>? penalty = _penalty;
        if (_avoid.Count > 0)
        {
            Func<int, float>? inner = _penalty;
            penalty = n => (_avoid.Contains(n) ? 5000f : 0f) + (inner?.Invoke(n) ?? 0f);
        }

        if (!g.FindPath(from, to, _path, _stealth, penalty, _world.BadLinks(_spy), float.MaxValue, _world.BadEdges(_spy)))
        {
            Failed = true;
            return false;
        }

        Plans++;

        // Walk to the start node first when the pawn, off it, cannot go straight to the first waypoint.
        FVector feet = Feet(pawn);
        if (from >= 0 && (_path.Count == 0 || !Clear(feet, _path[0].Point, pawn.bIsCrouched)))
        {
            _path.Insert(0, new NavStep(from, g.Node(from).Standing ? NavEdgeKind.Walk : NavEdgeKind.Crouch, -1, g.Node(from).Floor));
        }

        _path.Add(new NavStep(to, _path.Count > 0 ? _path[^1].Kind : NavEdgeKind.Walk, -1, _goal));
        return true;
    }

    /// <summary>The keys for this tick. <paramref name="viewYaw"/> is the controller's yaw now.</summary>
    public NavCommand Steer(SBasePawn pawn, float now, int viewYaw, bool sneak)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        var cmd = new NavCommand { Tolerance = 9000 };
        if (!_hasGoal || Failed)
        {
            return cmd;
        }

        FVector at = pawn.Location;
        FVector feet = Feet(pawn);
        if (_phase == TPhase.None && Horizontal(_goal - at) < 20f && MathF.Abs(_goal.Z - feet.Z) < 80f)
        {
            Arrived = true;
            return cmd;
        }

        Arrived = false;
        if (_phase == TPhase.None && pawn.Physics == EPhysics.PHYS_Ledge)
        {
            return Hanging(pawn, now);
        }

        _hangSince = -1f;
        if (now < _wiggleUntil)
        {
            return Wiggle(pawn, now);
        }

        if (_next >= _path.Count && !Plan(pawn))
        {
            return cmd;
        }

        if (!CheckProgress(pawn, now))
        {
            return cmd;
        }

        NavStep step = _path[_next];
        if (step.Link >= 0)
        {
            return Traverse(pawn, now, viewYaw, step);
        }

        // Pass waypoints: on arrival, or early when the one after is in a clear straight line and not a link.
        while (_next < _path.Count - 1)
        {
            NavStep cur = _path[_next];
            float d = Horizontal(cur.Point - at);
            bool landed = cur.Kind != NavEdgeKind.Drop || (pawn.Physics == EPhysics.PHYS_Walking && MathF.Abs(cur.Point.Z - feet.Z) < 60f);
            NavStep after = _path[_next + 1];
            if (landed && (d < 14f || (d < 40f && after.Link < 0 && after.Kind != NavEdgeKind.Drop && Clear(feet, after.Point, pawn.bIsCrouched))))
            {
                _next++;
                if (after.Link >= 0)
                {
                    return Traverse(pawn, now, viewYaw, after);
                }

                continue;
            }

            break;
        }

        step = _path[_next];
        FVector target = step.Point;
        int yaw = YawTo(at, target);
        cmd.Yaw = yaw;
        cmd.Tolerance = 9000;
        cmd.Keys.BT_Up = 1;
        cmd.Keys.FastSpeed = true;
        bool crawl = step.Kind == NavEdgeKind.Crouch || (_next + 1 < _path.Count && _path[_next + 1].Kind == NavEdgeKind.Crouch &&
            Horizontal(target - at) < 80f);
        cmd.Crouched = crawl ? true : (sneak && _spy ? true : (bool?)null);
        if (!_spy && !crawl)
        {
            cmd.Crouched = false;
        }

        // Off a ledge standing (a slow crouched walk off an edge turns into a back grab) and walking, not running, so
        // the fall comes down near the landing spot instead of far past it. While falling the keys are let go.
        bool dropNext = _next + 1 < _path.Count && _path[_next + 1].Kind == NavEdgeKind.Drop && Horizontal(target - at) < 96f;
        if (!crawl && (step.Kind == NavEdgeKind.Drop || dropNext))
        {
            cmd.Crouched = false;
            cmd.Keys.FastSpeed = false;
            if (pawn.Physics == EPhysics.PHYS_Falling)
            {
                cmd.Keys = default;
                cmd.Yaw = null;
                return cmd;
            }

            // A teammate under the landing spot: wait for him to move on (a body falling on a pawn drop-attacks it).
            NavStep drop = step.Kind == NavEdgeKind.Drop ? step : _path[_next + 1];
            // A teammate below the edge itself (hanging from it, or just under it) is in the way of the fall too.
            if (TeammateBelow(pawn, drop.Point, out SBasePawn? below) || TeammateBelow(pawn, at, out below))
            {
                if (_yieldSince < 0f)
                {
                    _yieldSince = now;
                }

                if (now - _yieldSince < AiK.DropYieldSeconds)
                {
                    cmd.Keys.BT_Up = 0;
                    _progressAt = now;
                    return cmd;
                }

                // He stays there (a mate hanging on the ledge below, or waiting at its foot): another way down without
                // that landing, else the goal is given up. (Before, the wait started again every other tick and the
                // walker stood at the edge for as long as the mate stayed.)
                _yieldSince = -1f;
                NavGraph g = Graph;
                for (int n = 0; n < g.NodeCount; n++)
                {
                    if (NearMate(g.Node(n).Floor, below!))
                    {
                        _avoid.Add(n);
                    }
                }

                if (++_replans > 6 || !Plan(pawn) || _path.Exists(s => s.Kind == NavEdgeKind.Drop && NearMate(s.Point, below!)))
                {
                    Failed = true;
                }

                return new NavCommand { Tolerance = 9000 };
            }
        }

        _yieldSince = -1f;

        // A pawn standing in the way (a mate waiting at a wall, a bot on the same corridor): step around him with a
        // side step away from his side of the line, instead of pushing into him until the stuck handling jumps (a
        // jump into a pawn lands on his head, which drop-attacks him).
        if (_phase == TPhase.None && PawnInTheWay(pawn, target, AiK.PawnAheadGap, out SBasePawn? blocker, out float side))
        {
            // A waypoint he stands on is passed: the walk goes on to the next one, round him.
            bool skipped = false;
            while (_next < _path.Count - 1 && _path[_next + 1].Link < 0 && _path[_next + 1].Kind != NavEdgeKind.Drop &&
                   Horizontal(blocker!.Location - target) < pawn.CollisionRadius + blocker.CollisionRadius + AiK.PawnPassMargin)
            {
                _next++;
                target = _path[_next].Point;
                skipped = true;
            }

            if (skipped)
            {
                cmd.Yaw = YawTo(at, target);
                if (!PawnInTheWay(pawn, target, AiK.PawnAheadGap, out blocker, out side))
                {
                    _sidestepSince = -1f;
                    return cmd;
                }
            }

            // The side is chosen once per pawn met: as the walker slides round him the line's side would flip.
            if (_sidestepSince < 0f || !ReferenceEquals(_sidestepFrom, blocker))
            {
                _sidestepSince = now;
                _sidestepFrom = blocker;
                _sidestepLeft = side > 0f;
            }

            if (now - _sidestepSince < AiK.PawnSidestepSeconds)
            {
                if (_sidestepLeft)
                {
                    cmd.Keys.BT_Left = 1;
                }
                else
                {
                    cmd.Keys.BT_Right = 1;
                }

                cmd.Keys.FastSpeed = false;
                _progressAt = now;
                return cmd;
            }
        }
        else
        {
            _sidestepSince = -1f;
            _sidestepFrom = null;
        }

        return cmd;
    }

    private float _yieldSince = -1f;
    private float _sidestepSince = -1f;
    private SBasePawn? _sidestepFrom;
    private bool _sidestepLeft;

    /// <summary>
    /// True when a living pawn's cylinder is ahead of <paramref name="pawn"/> on the line to <paramref name="target"/>,
    /// overlapping the walker's width (plus <see cref="AiK.PawnPassMargin"/>), with less than <paramref name="gap"/>
    /// between the cylinders (the nearest such pawn in <paramref name="blocker"/>). <paramref name="side"/> is positive
    /// when that pawn's centre is right of the line.
    /// </summary>
    private bool PawnInTheWay(SBasePawn pawn, FVector target, float gap, out SBasePawn? blocker, out float side)
    {
        side = 0f;
        blocker = null;
        float best = float.MaxValue;
        FVector to = target - pawn.Location;
        float len = Horizontal(to);
        if (len < 1f)
        {
            return false;
        }

        float dx = to.X / len;
        float dy = to.Y / len;
        foreach (SBasePawn p in _world.Level.DynamicActors<SBasePawn>())
        {
            if (ReferenceEquals(p, pawn) || p.bDeleteMe || p.Health <= 0 || !p.bCollideActors)
            {
                continue;
            }

            FVector d = p.Location - pawn.Location;
            if (MathF.Abs(d.Z) > pawn.CollisionHeight + p.CollisionHeight)
            {
                continue;
            }

            float along = (d.X * dx) + (d.Y * dy);
            float across = (d.Y * dx) - (d.X * dy);
            float reach = pawn.CollisionRadius + p.CollisionRadius;
            if (along <= 0f || along > len + p.CollisionRadius || MathF.Abs(across) >= reach + AiK.PawnPassMargin ||
                Horizontal(d) - reach > gap || along >= best)
            {
                continue;
            }

            best = along;
            blocker = p;
            side = across;
        }

        return blocker is not null;
    }

    /// <summary>A living pawn of the walker's side within reach of <paramref name="landing"/> and below the walker.</summary>
    private bool TeammateBelow(SBasePawn pawn, FVector landing) => TeammateBelow(pawn, landing, out _);

    private bool TeammateBelow(SBasePawn pawn, FVector landing, out SBasePawn? mate)
    {
        mate = null;
        foreach (SBasePawn p in _world.Level.DynamicActors<SBasePawn>())
        {
            if (ReferenceEquals(p, pawn) || p.bDeleteMe || p.Health <= 0 || (p is SPawnAttaque) != (pawn is SPawnAttaque))
            {
                continue;
            }

            FVector d = p.Location - landing;
            if (Horizontal(d) < AiK.DropYieldRadius && p.Location.Z < pawn.Location.Z - 60f && d.Z < 300f)
            {
                mate = p;
                return true;
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="point"/> is within the drop wait's reach of <paramref name="mate"/> (horizontal, near his height).</summary>
    private static bool NearMate(FVector point, SBasePawn mate) =>
        Horizontal(point - mate.Location) < AiK.DropYieldRadius && MathF.Abs(point.Z - mate.Location.Z) < 300f;

    private float _hangSince = -1f;
    private float _wiggleUntil = -1f;
    private int _wiggles;

    /// <summary>
    /// Hanging from a ledge the walk did not plan (a fall caught by the arms): climb when the next waypoint is on top,
    /// let go (the crouch key) when it is below or the climb does not come.
    /// </summary>
    private NavCommand Hanging(SBasePawn pawn, float now)
    {
        var cmd = new NavCommand { Tolerance = 65536, Busy = true };
        if (_hangSince < 0f)
        {
            _hangSince = now;
        }

        float lip = pawn.Location.Z + AiK.LedgeHangDrop;
        FVector target = _next < _path.Count ? _path[_next].Point : _goal;
        bool climb = target.Z > lip - 60f && now - _hangSince < 2.5f;
        if (climb)
        {
            cmd.Keys.BT_Up = 1;
            cmd.Keys.FastSpeed = true;
        }
        else
        {
            // Back on the stick lets go (OnLedge.PlayerMove: iMove back sets bCrouchWithRoll, then SA_LEDGEFALL); the
            // crouch key's latch only toggles where the state reads it.
            cmd.Keys.BT_Down = 1;
            if (((int)((now - _hangSince) * 4f) & 1) == 0)
            {
                cmd.Keys.bCrouch = 1;
            }
        }

        _progressAt = -1f;
        return cmd;
    }

    /// <summary>
    /// A short burst of keys that frees a pawn stuck on geometry: forward, a side step, a jump (left out next to another
    /// pawn, whose head the jump would land on).
    /// </summary>
    private NavCommand Wiggle(SBasePawn pawn, float now)
    {
        var cmd = new NavCommand { Tolerance = 65536 };
        cmd.Keys.BT_Up = 1;
        cmd.Keys.FastSpeed = true;
        if ((_wiggles & 1) == 0)
        {
            cmd.Keys.BT_Left = 1;
        }
        else
        {
            cmd.Keys.BT_Right = 1;
        }

        if (_wiggleUntil - now > 0.9f && !PawnNear(pawn, AiK.PawnNoJumpGap))
        {
            cmd.Keys.Press(AiCommand.Jump);
        }

        cmd.Crouched = false;
        return cmd;
    }

    /// <summary>True when another living pawn's cylinder is within <paramref name="gap"/> of this one's (horizontal, at a shared height).</summary>
    private bool PawnNear(SBasePawn pawn, float gap)
    {
        foreach (SBasePawn p in _world.Level.DynamicActors<SBasePawn>())
        {
            if (ReferenceEquals(p, pawn) || p.bDeleteMe || p.Health <= 0 || !p.bCollideActors)
            {
                continue;
            }

            FVector d = p.Location - pawn.Location;
            if (MathF.Abs(d.Z) <= pawn.CollisionHeight + p.CollisionHeight + 40f &&
                Horizontal(d) - pawn.CollisionRadius - p.CollisionRadius < gap)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Starts a one second wiggle (stuck, with no plan that helps).</summary>
    public void StartWiggle(float now)
    {
        _wiggles++;
        _wiggleUntil = now + 1f;
        _progressAt = -1f;
    }

    private bool CheckProgress(Pawn pawn, float now)
    {
        if (_phase != TPhase.None)
        {
            return true;
        }

        float remaining = Remaining(pawn.Location);
        if (_progressAt < 0f || remaining < _bestRemaining - 24f)
        {
            _progressAt = now;
            _bestRemaining = remaining;
            return true;
        }

        if (now - _progressAt <= 2f)
        {
            return true;
        }

        // Stuck where it stuck a little while ago (a closed door, a mover, a wall the graph thinks is open): the edges
        // across the way ahead are left out of the side's plans for a while, so this and later plans go round (a pipe,
        // another door) even after the goal is set again.
        FVector at = pawn.Location;
        bool again = _stuckTime >= 0f && now - _stuckTime < AiK.StuckRepeatSeconds && Horizontal(at - _stuckAt) < AiK.StuckRepeatRadius &&
            MathF.Abs(at.Z - _stuckAt.Z) < 80f;
        _stuckAt = at;
        _stuckTime = now;
        if (again && pawn is SBasePawn walker && BlockAhead(walker))
        {
            _stuckTime = -1f;
            if (++_replans > 6 || !Plan(pawn))
            {
                Failed = true;
                return false;
            }

            return true;
        }

        // Stuck: every other time wiggle free first, else avoid the place ahead and plan again from here; after a
        // few tries the goal is given up.
        if ((_replans & 1) == 0)
        {
            _replans++;
            StartWiggle(now);
            return false;
        }

        if (_next < _path.Count)
        {
            _avoid.Add(_path[_next].Node);
        }

        if (++_replans > 6 || !Plan(pawn))
        {
            Failed = true;
            return false;
        }

        return true;
    }

    private FVector _stuckAt;
    private float _stuckTime = -1f;
    private readonly List<int> _across = new();

    /// <summary>
    /// The way ahead is shut: the edges across a line in front of the walker are left out of the side's plans (a
    /// closed door hit by a trace toward the next waypoint gives a wider line along its face). False when there is
    /// nothing to block (no waypoint ahead, or a pawn in the way: he moves on).
    /// </summary>
    private bool BlockAhead(SBasePawn pawn)
    {
        FVector at = pawn.Location;
        int k = _next;
        while (k < _path.Count - 1 && Horizontal(_path[k].Point - at) < 24f)
        {
            k++;
        }

        if (k >= _path.Count || PawnInTheWay(pawn, _path[k].Point, AiK.PawnAheadGap, out _, out _))
        {
            return false;
        }

        FVector to = _path[k].Point - at;
        float len = Horizontal(to);
        if (len < 1f)
        {
            return false;
        }

        var dir = new FVector(to.X / len, to.Y / len, 0f);
        FVector end = at + dir * AiK.StuckProbe;
        FVector center;
        FVector normal;
        float half;
        if (_world.Collision.LineCheck(out FCheckResult hit, pawn, end, at, default, BarrierFlags))
        {
            center = hit.Location;
            normal = new FVector(hit.Normal.X, hit.Normal.Y, 0f);
            normal = normal.Size() > 0.3f ? normal.SafeNormal() : -dir;
            half = hit.Actor is Mover ? AiK.DoorBlockHalfWidth : AiK.StuckBlockHalfWidth;
        }
        else
        {
            center = at + dir * MathF.Min(len, 48f);
            normal = -dir;
            half = AiK.StuckBlockHalfWidth;
        }

        return BlockAcross(Feet(pawn).Z, center, normal, half) > 0;
    }

    /// <summary>
    /// Leaves out of the side's plans (for <see cref="AiK.BlockedEdgeSeconds"/>) the edges that cross the line through
    /// <paramref name="center"/> along a face whose normal is <paramref name="normal"/>, <paramref name="halfWidth"/>
    /// each way, near the floor height <paramref name="floorZ"/>. Returns the count of edges left out.
    /// </summary>
    public int BlockAcross(float floorZ, FVector center, FVector normal, float halfWidth)
    {
        var along = new FVector(-normal.Y, normal.X, 0f);
        float size = along.Size();
        if (size < 0.01f)
        {
            return 0;
        }

        along *= halfWidth / size;
        Graph.EdgesAcross(center - along, center + along, floorZ, 60f, _across);
        if (_across.Count > 0)
        {
            _world.MarkBadEdges(_spy, _across);
        }

        return _across.Count;
    }

    /// <summary>Plans again from here (after <see cref="BlockAcross"/>); false when the goal cannot be reached now.</summary>
    public bool Replan(Pawn pawn)
    {
        ArgumentNullException.ThrowIfNull(pawn);
        if (!_hasGoal)
        {
            return false;
        }

        if (++_replans > 6 || !Plan(pawn))
        {
            Failed = true;
            return false;
        }

        return true;
    }

    /// <summary>The walk goes on after a pause the follower did not steer (a door opened on the way): no stuck count for it.</summary>
    public void Resume()
    {
        _progressAt = -1f;
        _wiggleUntil = -1f;
    }

    // ---- traversal -------------------------------------------------------------------------------------------

    private NavCommand Traverse(SBasePawn pawn, float now, int viewYaw, NavStep step)
    {
        NavLink link = Graph.Links[step.Link];
        var cmd = new NavCommand { Busy = true, Crouched = false, Tolerance = 9000 };
        FVector at = pawn.Location;
        FVector feet = Feet(pawn);
        if (_phase == TPhase.None)
        {
            Enter(TPhase.Approach, now);
            _tries = 0;
            _lined = false;
            _crossed = false;
        }

        switch (_phase)
        {
            case TPhase.Approach:
            {
                // Under a bar: first to a spot behind the jump spot, then walk in along the bar, so the body faces
                // the way the hands will go (the move along the bar follows the body, not the view).
                FVector spot = link.Start;
                if (link.Kind == NavEdgeKind.HandOverHand && !_lined)
                {
                    spot = link.Start - YawVector(link.Yaw) * AiK.HohRunIn;
                    if (Horizontal(spot - at) < 14f)
                    {
                        _lined = true;
                        spot = link.Start;
                    }
                }

                float d = Horizontal(spot - at);
                if (link.Kind == NavEdgeKind.HandOverHand && _lined && d > 8f)
                {
                    cmd.Yaw = YawTo(at, spot);
                    cmd.Tolerance = 2500;
                    cmd.Keys.BT_Up = 1;
                    if (now - _phaseAt > 10f)
                    {
                        return Fail(pawn, link, step);
                    }

                    return cmd;
                }

                if (d > 12f)
                {
                    // Close in slowly and only when facing the spot, so a running pawn does not overshoot it.
                    cmd.Yaw = YawTo(at, spot);
                    cmd.Tolerance = d > 120f ? 6000 : 2500;
                    cmd.Keys.BT_Up = (byte)(d > 40f || Speed(pawn) < 120f ? 1 : 0);
                    cmd.Keys.FastSpeed = d > 100f;
                    if (now - _phaseAt > 8f)
                    {
                        return Fail(pawn, link, step);
                    }

                    return cmd;
                }

                Enter(TPhase.Face, now);
                goto case TPhase.Face;
            }

            case TPhase.Face:
                cmd.Yaw = link.Yaw;

                // A stance change blocks the zip line grab for 2 s (m_LastSpecialMoveTime, docs/spikes/R4-traversal.md).
                bool cooling = link.Kind == NavEdgeKind.ZipLine && pawn.Controller is SPlayerController spc &&
                    now - spc.m_LastSpecialMoveTime < 2.1f;
                if (pawn.bIsCrouched || YawError(viewYaw, link.Yaw) > 300 || !Still(pawn) || cooling)
                {
                    if (now - _phaseAt > 4.5f)
                    {
                        return Fail(pawn, link, step);
                    }

                    return cmd;
                }

                Enter(link.Kind switch
                {
                    NavEdgeKind.ZipLine => TPhase.RunUp,
                    NavEdgeKind.HandOverHand => TPhase.Settle,
                    _ => TPhase.Press,
                }, now);
                _slowSince = -1f;
                return cmd;

            case TPhase.Press:
                cmd.Yaw = link.Yaw;
                cmd.Keys.BT_Up = 1;
                cmd.Keys.FastSpeed = true;
                if (link.Kind is NavEdgeKind.PipeClimb or NavEdgeKind.LadderUp or NavEdgeKind.LadderDown or NavEdgeKind.FenceClimb)
                {
                    // Walk into the pipe or ladder (the arms zone grabs it), walk off toward the ladder from its top
                    // (the feet zone hangs the pawn on), run into the fence (the wall hit grabs it).
                    if (link.Kind == NavEdgeKind.LadderDown)
                    {
                        cmd.Keys.FastSpeed = false;
                    }

                    bool on = link.Kind switch
                    {
                        NavEdgeKind.PipeClimb => pawn.Physics == EPhysics.PHYS_OnPipe,
                        NavEdgeKind.LadderUp => pawn.Physics == EPhysics.PHYS_OnLadder || InState(pawn, "s_GrabLadder"),
                        NavEdgeKind.FenceClimb => pawn.Physics == EPhysics.PHYS_OnVerticalPlane || InState(pawn, "GrabFence") ||
                            InState(pawn, "OnFence"),
                        _ => pawn.Physics is not (EPhysics.PHYS_Walking or EPhysics.PHYS_Falling),
                    };
                    if (on)
                    {
                        Enter(TPhase.Climb, now);
                    }
                    else if (now - _phaseAt > 3f || (link.Kind == NavEdgeKind.LadderDown && pawn.Physics == EPhysics.PHYS_Falling))
                    {
                        return Fail(pawn, link, step);
                    }

                    return cmd;
                }

                // A ledge: run to the wall until blocked, then let go so the jump starts from a stop.
                if (Speed(pawn) < 20f && now - _phaseAt > 0.2f)
                {
                    _slowSince = _slowSince < 0f ? now : _slowSince;
                }
                else
                {
                    _slowSince = -1f;
                }

                if ((_slowSince >= 0f && now - _slowSince > 0.1f) || now - _phaseAt > 1.2f)
                {
                    Enter(TPhase.Settle, now);
                    cmd.Keys.BT_Up = 0;
                }

                return cmd;

            case TPhase.Settle:
                cmd.Yaw = link.Yaw;
                if (now - _phaseAt < 0.25f || !Still(pawn))
                {
                    if (now - _phaseAt > 2f)
                    {
                        return Fail(pawn, link, step);
                    }

                    return cmd;
                }

                cmd.Keys.Press(AiCommand.Jump);
                Enter(TPhase.Air, now);
                return cmd;

            case TPhase.RunUp:
                cmd.Yaw = link.Yaw;
                if (now - _phaseAt < 0.2f)
                {
                    cmd.Keys.BT_Up = 1;
                    cmd.Keys.FastSpeed = true;
                    return cmd;
                }

                cmd.Keys.Press(AiCommand.Jump);
                Enter(TPhase.Air, now);
                return cmd;

            case TPhase.Air:
                cmd.Tolerance = 65536;
                if (pawn.Physics == EPhysics.PHYS_OnPipeH || InState(pawn, "s_GrabPipH"))
                {
                    Enter(TPhase.Bar, now);
                    _slowSince = -1f;
                    return cmd;
                }

                if (pawn.Physics == EPhysics.PHYS_Ledge)
                {
                    Enter(TPhase.Hang, now);
                    goto case TPhase.Hang;
                }

                if (pawn.Physics == EPhysics.PHYS_Linear)
                {
                    Enter(TPhase.Ride, now);
                    return cmd;
                }

                if (now - _phaseAt > 0.4f && pawn.Physics == EPhysics.PHYS_Walking)
                {
                    // Missed: try once more from the start spot.
                    if (++_tries > 1)
                    {
                        return Fail(pawn, link, step);
                    }

                    _lined = false;
                    Enter(TPhase.Approach, now);
                }
                else if (now - _phaseAt > 3f)
                {
                    return Fail(pawn, link, step);
                }

                return cmd;

            case TPhase.Hang:
                cmd.Tolerance = 65536;
                cmd.Keys.BT_Up = 1;
                cmd.Keys.FastSpeed = true;
                if (pawn.Physics == EPhysics.PHYS_Walking && now - _phaseAt > 0.3f)
                {
                    return Done(pawn, link, step);
                }

                if (now - _phaseAt > 5f)
                {
                    // Let go (the crouch key drops from a hang) and give the link up.
                    cmd.Keys.BT_Up = 0;
                    cmd.Keys.bCrouch = 1;
                    return Fail(pawn, link, step, cmd);
                }

                return cmd;

            case TPhase.Climb:
            {
                cmd.Tolerance = 65536;
                cmd.Keys.FastSpeed = true;

                // Up the pipe or ladder; down a ladder from its top; up a fence, over the top, then down the far side
                // (back on the stick climbs down until the feet land).
                bool down = link.Kind == NavEdgeKind.LadderDown;
                if (link.Kind == NavEdgeKind.FenceClimb)
                {
                    FVector mid = (link.Start + link.End) * 0.5f;
                    FVector face = YawVector(link.Yaw);
                    _crossed |= ((at.X - mid.X) * face.X) + ((at.Y - mid.Y) * face.Y) > 0f;
                    down = _crossed;
                }

                if (down)
                {
                    cmd.Keys.BT_Down = 1;
                }
                else
                {
                    cmd.Keys.BT_Up = 1;
                }

                float t = now - _phaseAt;
                if (pawn.Physics == EPhysics.PHYS_Walking && t > 0.5f && (MathF.Abs(feet.Z - link.End.Z) < 80f || t > 2.5f))
                {
                    return Done(pawn, link, step);
                }

                float speed = link.Kind switch
                {
                    NavEdgeKind.PipeClimb => AiK.PipeClimbSpeed,
                    NavEdgeKind.FenceClimb => AiK.FenceSpeed * 0.5f,
                    _ => AiK.LadderSpeed,
                };
                if (t > link.Height / speed + 8f)
                {
                    return Fail(pawn, link, step);
                }

                return cmd;
            }

            case TPhase.Bar:
            {
                // Hand over hand to the far end (forward), then the crouch button lets go.
                cmd.Tolerance = 65536;
                float t = now - _phaseAt;
                bool atEnd = Horizontal(at - link.End) < 24f;
                if (Speed(pawn) < 10f && t > 0.8f)
                {
                    _slowSince = _slowSince < 0f ? now : _slowSince;
                }
                else
                {
                    _slowSince = -1f;
                }

                if (pawn.Physics is EPhysics.PHYS_OnPipeH or EPhysics.PHYS_RootMotion || InState(pawn, "s_GrabPipH") || InState(pawn, "s_OnPipeH"))
                {
                    if (atEnd || (_slowSince >= 0f && now - _slowSince > 0.4f))
                    {
                        cmd.Keys.bCrouch = 1;
                    }
                    else
                    {
                        cmd.Keys.BT_Up = 1;
                    }
                }
                else if (pawn.Physics == EPhysics.PHYS_Walking && t > 0.5f)
                {
                    return Done(pawn, link, step);
                }

                if (t > (link.Start - link.End).Size() / AiK.HohSpeed + 8f)
                {
                    cmd.Keys.bCrouch = 1;
                    return Fail(pawn, link, step, cmd);
                }

                return cmd;
            }

            case TPhase.Ride:
                cmd.Tolerance = 65536;
                if (pawn.Physics == EPhysics.PHYS_Ledge)
                {
                    // A ride that ends over a ledge catches it (zip line 29): climb it.
                    Enter(TPhase.Hang, now);
                    cmd.Keys.BT_Up = 1;
                    return cmd;
                }

                if (pawn.Physics == EPhysics.PHYS_Walking && now - _phaseAt > 0.5f)
                {
                    return Done(pawn, link, step);
                }

                if (now - _phaseAt > (link.Start - link.End).Size() / 250f + 8f)
                {
                    return Fail(pawn, link, step);
                }

                return cmd;
        }

        _ = feet;
        return cmd;
    }

    private bool _lined;
    private bool _crossed;

    private static FVector YawVector(int yaw) => new FRotator(0, yaw, 0).Vector();

    /// <summary>True when the pawn's controller is in a state whose name starts with <paramref name="prefix"/>.</summary>
    private static bool InState(Pawn pawn, string prefix) =>
        pawn.Controller is { } c && c.GetStateName().ToString().StartsWith(prefix, StringComparison.Ordinal);

    private void Enter(TPhase phase, float now)
    {
        _phase = phase;
        _phaseAt = now;
    }

    private NavCommand Done(SBasePawn pawn, NavLink link, NavStep step)
    {
        _phase = TPhase.None;
        LinksDone++;
        _progressAt = -1f;
        if (Horizontal(pawn.Location - link.End) > 160f)
        {
            // Off the expected landing: plan again from here.
            Plan(pawn);
        }
        else
        {
            _next++;
        }

        return new NavCommand { Tolerance = 9000 };
    }

    private NavCommand Fail(SBasePawn pawn, NavLink link, NavStep step, NavCommand? last = null)
    {
        _phase = TPhase.None;
        LinksFailed++;
        _world.MarkBadLink(_spy, step.Link);
        if (++_replans > 6 || !Plan(pawn))
        {
            Failed = true;
        }

        return last ?? new NavCommand { Tolerance = 9000 };
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private static bool Still(Pawn pawn) => Speed(pawn) < 5f && pawn.Physics == EPhysics.PHYS_Walking;

    private static float Speed(Pawn pawn) => Horizontal(pawn.Velocity);

    public static FVector Feet(Pawn pawn) => new(pawn.Location.X, pawn.Location.Y, pawn.Location.Z - pawn.CollisionHeight);

    /// <summary>A swept body (crouched or standing) from one floor point to another on the same level, with ground all along.</summary>
    public bool Clear(FVector fromFloor, FVector toFloor, bool crouched)
    {
        NavProfile p = Graph.Profile;
        if (MathF.Abs(toFloor.Z - fromFloor.Z) > p.StepUp * 0.5f)
        {
            return false;
        }

        float h = crouched ? p.CrouchHeight : p.StandingHeight;
        float lift = h + p.StepUp * 0.25f + 2f;
        var body = new FVector(p.Radius, p.Radius, h - p.StepUp * 0.25f);
        var from = new FVector(fromFloor.X, fromFloor.Y, MathF.Max(fromFloor.Z, toFloor.Z) + lift);
        var to = new FVector(toFloor.X, toFloor.Y, from.Z);
        if (_world.Collision.LineCheck(out _, null, to, from, body, WorldFlags))
        {
            return false;
        }

        FVector d = to - from;
        float len = Horizontal(d);
        int steps = Math.Max(1, (int)(len / (p.Spacing * 0.5f)));
        for (int k = 1; k < steps; k++)
        {
            FVector q = from + d * (k / (float)steps);
            var top = new FVector(q.X, q.Y, fromFloor.Z + 8f);
            var bottom = new FVector(q.X, q.Y, fromFloor.Z - p.StepUp - 8f);
            if (!_world.Collision.LineCheck(out _, null, bottom, top, default, WorldFlags))
            {
                return false;
            }
        }

        return true;
    }

    public static int YawTo(FVector from, FVector to)
    {
        FVector d = to - from;
        return new FVector(d.X, d.Y, 0f).Rotation().Yaw & 0xFFFF;
    }

    /// <summary>The absolute difference of two yaws (0 to 32768).</summary>
    public static int YawError(int a, int b)
    {
        int d = (a - b) & 0xFFFF;
        return d > 32768 ? 65536 - d : d;
    }

    public static float Horizontal(FVector d) => MathF.Sqrt(d.X * d.X + d.Y * d.Y);
}
