using System;
using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Physics.Collision;

namespace OpenScct.Game.AI;

/// <summary>
/// Builds a <see cref="NavGraph"/> from a map's collision and GEs, with nothing stored about the map beyond the cache
/// the caller keeps:
/// <list type="bullet">
/// <item>floors: a small box traced down each column of a grid (as the walking pawn's floor checks do), walkable when
/// the normal's Z is at least MINFLOORZ, a node where the crouched body fits (flagged standing when the standing
/// one fits too);</item>
/// <item>walk and crouch edges to the neighbouring nodes a swept body reaches with at most a step (MAXSTEPHEIGHT) up,
/// standing when both ends and the sweep fit a standing pawn, crouched otherwise (vents, crawl spaces);</item>
/// <item>drops off ledges, one way, cleared all the way down and no deeper than the side's fall damage height;</item>
/// <item>the traversal links of the active GEs: for both sides ladders (LADDER), up from the foot and down from the
/// top; for spies also ledges a standing jump catches (LG), vertical pipes (PIPE) from the foot to the floor beside
/// the top, zip lines (ZL) from a run-up under the high end to the floor under the low end, horizontal pipes (HOH)
/// a standing jump catches, hand over hand to the far end and a drop, and fences (FENCE, the climbable fence's top
/// line) from the floor on one side to the floor on the other. Poles get no links (the movement port has no pole
/// states, and no converted map has a POLE GE).</item>
/// </list>
/// </summary>
public sealed class NavBuilder
{
    // What a walking pawn collides with, without other pawns and movers (doors open as they are reached).
    private const ETraceFlags WorldFlags = ETraceFlags.TRACE_Blocking | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry |
        ETraceFlags.TRACE_Volumes | ETraceFlags.TRACE_Others;

    private static readonly FVector FloorProbe = new(4f, 4f, 2f);

    private readonly ICollisionWorld _world;
    private readonly NavProfile _profile;
    private readonly FVector _min;
    private readonly FVector _max;
    private readonly int _nx;
    private readonly int _ny;
    private readonly List<NavNode> _nodes = new();
    private readonly Dictionary<long, List<int>> _columns = new();
    private readonly List<List<(int To, NavEdgeKind Kind, float Cost, int Link)>> _edges = new();
    private readonly List<NavLink> _links = new();

    private NavBuilder(ICollisionWorld world, NavProfile profile, FVector min, FVector max)
    {
        _world = world;
        _profile = profile;
        _min = min;
        _max = max;
        _nx = Math.Max(1, (int)((max.X - min.X) / profile.Spacing) + 1);
        _ny = Math.Max(1, (int)((max.Y - min.Y) / profile.Spacing) + 1);
    }

    /// <summary>Statistics of the last build (diagnostics).</summary>
    public sealed record Stats(int Columns, int Nodes, int Edges, int Links);

    /// <summary>
    /// Builds the graph of <paramref name="world"/> between <paramref name="min"/> and <paramref name="max"/>, with the
    /// links of the GEs active under <paramref name="activationMask"/>.
    /// </summary>
    public static NavGraph Build(ICollisionWorld world, FVector min, FVector max, NavProfile profile, IReadOnlyList<GERecord>? ges = null,
        byte activationMask = 0)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(profile);
        var b = new NavBuilder(world, profile, min, max);
        b.SampleFloors();
        b.Connect();
        if (ges is not null)
        {
            b.AddLinks(ges, activationMask);
        }

        return b.Finish();
    }

    /// <summary>The box around <paramref name="points"/> widened by <paramref name="margin"/> (X, Y, below and above).</summary>
    public static (FVector Min, FVector Max) BoundsOf(IEnumerable<FVector> points, FVector margin)
    {
        ArgumentNullException.ThrowIfNull(points);
        bool any = false;
        FVector min = default;
        FVector max = default;
        foreach (FVector p in points)
        {
            min = any ? new FVector(MathF.Min(min.X, p.X), MathF.Min(min.Y, p.Y), MathF.Min(min.Z, p.Z)) : p;
            max = any ? new FVector(MathF.Max(max.X, p.X), MathF.Max(max.Y, p.Y), MathF.Max(max.Z, p.Z)) : p;
            any = true;
        }

        return (min - margin, max + margin);
    }

    // ---- floors ----------------------------------------------------------------------------------------------

    private static long Key(int ix, int iy) => ((long)ix << 32) | (uint)iy;

    private FVector ColumnXY(int ix, int iy) => new(_min.X + ix * _profile.Spacing, _min.Y + iy * _profile.Spacing, 0f);

    /// <summary>The box a body of half height <paramref name="h"/> tests with, from half a step above the feet to the head.</summary>
    private FVector Body(float h) => new(_profile.Radius, _profile.Radius, h - _profile.StepUp * 0.25f);

    private float Lift(float h) => h + _profile.StepUp * 0.25f + 2f;

    private bool Fits(FVector floor, float h) => !_world.PointCheck(out _, null, floor + new FVector(0f, 0f, Lift(h)), Body(h), WorldFlags);

    private void SampleFloors()
    {
        float bottom = _min.Z;
        float top = _max.Z;
        for (int ix = 0; ix < _nx; ix++)
        {
            for (int iy = 0; iy < _ny; iy++)
            {
                FVector c = ColumnXY(ix, iy);
                float z = top;
                List<int>? column = null;
                for (int guard = 0; guard < 32 && z > bottom; guard++)
                {
                    var start = new FVector(c.X, c.Y, z);
                    var end = new FVector(c.X, c.Y, bottom);
                    if (!_world.LineCheck(out FCheckResult hit, null, end, start, FloorProbe, WorldFlags))
                    {
                        break;
                    }

                    float floorZ = hit.Location.Z - FloorProbe.Z;
                    if (hit.Normal.Z >= _profile.MinFloorNormalZ)
                    {
                        var floor = new FVector(c.X, c.Y, floorZ);
                        if (Fits(floor, _profile.CrouchHeight))
                        {
                            column ??= new List<int>(2);
                            column.Add(_nodes.Count);
                            _nodes.Add(new NavNode(floor, Fits(floor, _profile.StandingHeight)));
                        }
                    }

                    // Continue below this surface: step down out of the solid it belongs to.
                    z = floorZ - 2f;
                    for (int dig = 0; dig < 64 && z > bottom && _world.PointCheck(out _, null, new FVector(c.X, c.Y, z), new FVector(1f, 1f, 1f), WorldFlags); dig++)
                    {
                        z -= 8f;
                    }
                }

                if (column is not null)
                {
                    _columns[Key(ix, iy)] = column;
                }
            }
        }

        for (int i = 0; i < _nodes.Count; i++)
        {
            _edges.Add(new List<(int, NavEdgeKind, float, int)>(8));
        }
    }

    // ---- edges -----------------------------------------------------------------------------------------------

    private void Connect()
    {
        float s = _profile.Spacing;
        Span<(int dx, int dy)> dirs = stackalloc (int, int)[] { (1, 0), (0, 1), (1, 1), (1, -1) };
        for (int n = 0; n < _nodes.Count; n++)
        {
            FVector a = _nodes[n].Floor;
            int ix = (int)MathF.Round((a.X - _min.X) / s);
            int iy = (int)MathF.Round((a.Y - _min.Y) / s);
            foreach ((int dx, int dy) in dirs)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    for (int k = 1; k <= 3; k++)
                    {
                        int cx = ix + sign * dx * k;
                        int cy = iy + sign * dy * k;
                        if (!_columns.TryGetValue(Key(cx, cy), out List<int>? column))
                        {
                            continue;
                        }

                        // Each walk pair once (the lower index tests it), each drop once (its higher end tests it);
                        // drops reach up to three cells out.
                        foreach (int m in column)
                        {
                            TryPair(n, m, k, dx != 0 && dy != 0);
                        }
                    }
                }
            }
        }
    }

    private void TryPair(int n, int m, int k, bool diagonal)
    {
        FVector a = _nodes[n].Floor;
        FVector b = _nodes[m].Floor;
        float dz = b.Z - a.Z;
        float horizontal = k * _profile.Spacing * (diagonal ? 1.4142f : 1f);
        if (MathF.Abs(dz) <= _profile.StepUp)
        {
            if (m <= n || k > 1 && (MathF.Abs(dz) < 1f || !ContinuousStep(a, b, k)))
            {
                return;
            }

            bool standing = _nodes[n].Standing && _nodes[m].Standing && Sweep(a, b, MathF.Max(a.Z, b.Z), _profile.StandingHeight);
            if (standing)
            {
                AddBoth(n, m, NavEdgeKind.Walk, horizontal);
            }
            else if (Sweep(a, b, MathF.Max(a.Z, b.Z), _profile.CrouchHeight))
            {
                AddBoth(n, m, NavEdgeKind.Crouch, horizontal * (AiK.RunSpeed / AiK.CrouchSpeed));
            }

            return;
        }

        // A drop from the higher to the lower one (the pair is seen from both sides; only the higher end tests it).
        if (dz > 0f)
        {
            return;
        }

        if (-dz > _profile.MaxDrop)
        {
            return;
        }

        float h = _profile.CrouchHeight;
        var over = new FVector(b.X, b.Y, a.Z + Lift(h));
        if (_world.LineCheck(out _, null, over, a + new FVector(0f, 0f, Lift(h)), Body(h), WorldFlags) ||
            _world.LineCheck(out _, null, b + new FVector(0f, 0f, Lift(h)), over, Body(h), WorldFlags))
        {
            return;
        }

        // Only off an edge: the floor the walker stands on must end before the drop column (else it walks on).
        if (k > 1 && HasFloorAtLevel(a, b, k))
        {
            return;
        }

        // Room to land: the landing floor goes on past the landing node, since a walker leaves the edge with some
        // speed and comes down beyond it (a narrow shelf over a deeper drop catches a pawn by the arms instead).
        var along = new FVector(b.X - a.X, b.Y - a.Y, 0f);
        float alongLength = along.Size();
        if (alongLength > 1f)
        {
            along *= 1f / alongLength;
            for (int r = 1; r <= 2; r++)
            {
                FVector q = b + along * (r * _profile.Spacing);
                if (FloorNear(q, b.Z + _profile.StepUp, b.Z - _profile.StepUp) is null)
                {
                    return;
                }
            }
        }

        float hurt = -dz > _profile.SafeDrop ? (-dz - _profile.SafeDrop) * 6f : 0f;
        _edges[n].Add((m, NavEdgeKind.Drop, horizontal + -dz * 0.5f + _profile.Spacing + hurt, -1));
    }

    private bool HasFloorAtLevel(FVector a, FVector b, int k)
    {
        for (int i = 1; i < k; i++)
        {
            FVector p = a + (b - a) * (i / (float)k);
            var top = new FVector(p.X, p.Y, a.Z + 8f);
            var bottom = new FVector(p.X, p.Y, a.Z - _profile.StepUp - 8f);
            if (_world.LineCheck(out _, null, bottom, top, default, WorldFlags))
            {
                return true;
            }
        }

        return false;
    }

    private bool Sweep(FVector a, FVector b, float z, float h)
    {
        var from = new FVector(a.X, a.Y, z + Lift(h));
        var to = new FVector(b.X, b.Y, z + Lift(h));
        return !_world.LineCheck(out _, null, to, from, Body(h), WorldFlags);
    }

    /// <summary>
    /// A short step can leave a hole in sampled nodes: the lower cylinder overlaps the riser, even though a walking
    /// pawn steps across it. Bridge only continuous support within MAXSTEPHEIGHT, with the usual body sweep.
    /// </summary>
    private bool ContinuousStep(FVector a, FVector b, int cells)
    {
        float last = a.Z;
        int samples = cells * 2;
        for (int i = 1; i <= samples; i++)
        {
            FVector point = a + (b - a) * (i / (float)samples);
            if (FloorNear(point, last + _profile.StepUp + 2f, last - _profile.StepUp - 2f) is not { } height)
            {
                return false;
            }

            last = height;
        }

        return MathF.Abs(last - b.Z) < 2f;
    }

    private void AddBoth(int n, int m, NavEdgeKind kind, float cost)
    {
        _edges[n].Add((m, kind, cost, -1));
        _edges[m].Add((n, kind, cost, -1));
    }

    // ---- traversal links -------------------------------------------------------------------------------------

    private void AddLinks(IReadOnlyList<GERecord> ges, byte mask)
    {
        foreach (GERecord ge in ges)
        {
            if (!ge.IsActive(mask) || ge.Platform == GEPlatform.PS2Only)
            {
                continue;
            }

            // Ladders are the only traversal both sides use (spec/movement/ladder.md).
            if (ge.Kind == GEKind.LADDER)
            {
                AddLadder(ge);
                continue;
            }

            if (!_profile.Traversal)
            {
                continue;
            }

            switch (ge.Kind)
            {
                case GEKind.LG:
                    AddLedge(ge);
                    break;
                case GEKind.PIPE:
                    AddPipe(ge);
                    break;
                case GEKind.ZL:
                    AddZip(ge);
                    break;
                case GEKind.HOH:
                    AddHandOverHand(ge);
                    break;
                case GEKind.FENCE:
                    AddFence(ge);
                    break;
            }
        }
    }

    private void AddLedge(GERecord ge)
    {
        FVector p0 = ge.P0;
        FVector p1 = ge.P1;
        var along = new FVector(p1.X - p0.X, p1.Y - p0.Y, 0f);
        float length = along.Size();
        if (length < 2f * (AiK.LedgeEndMargin + _profile.Radius) || MathF.Abs(p1.Z - p0.Z) > 8f)
        {
            return;
        }

        along *= 1f / length;
        var side = new FVector(-along.Y, along.X, 0f);
        float lip = (p0.Z + p1.Z) * 0.5f;

        // Which side is the top: a floor at the lip's height just behind it.
        FVector mid = (p0 + p1) * 0.5f;
        FVector inward;
        if (FloorNear(mid + side * 40f, lip + 30f, lip - 40f) is not null)
        {
            inward = side;
        }
        else if (FloorNear(mid - side * 40f, lip + 30f, lip - 40f) is not null)
        {
            inward = -side;
        }
        else
        {
            return;
        }

        FVector outward = -inward;
        int yaw = FaceYaw(inward);
        int lastBottom = -1;
        float margin = AiK.LedgeEndMargin + _profile.Radius + 8f;
        int samples = Math.Clamp((int)((length - 2f * margin) / 96f) + 1, 1, 6);
        for (int i = 0; i < samples; i++)
        {
            float t = samples == 1 ? length * 0.5f : margin + (length - 2f * margin) * (i / (float)(samples - 1));
            FVector at = p0 + along * t;
            FVector approach = at + outward * 45f;
            float? bottomZ = FloorNear(approach, lip - 20f, lip - AiK.LedgeJumpMax - 10f);
            if (bottomZ is not { } bz)
            {
                continue;
            }

            float height = lip - bz;
            if (height < AiK.LedgeJumpMin || height > AiK.LedgeJumpMax)
            {
                continue;
            }

            // There must be a wall under the lip to jump at.
            var standAt = new FVector(approach.X, approach.Y, bz + Lift(_profile.StandingHeight));
            if (!_world.LineCheck(out _, null, standAt + inward * 60f, standAt, Body(_profile.StandingHeight), WorldFlags))
            {
                continue;
            }

            int bottom = NearestTo(new FVector(approach.X, approach.Y, bz), 40f);
            int topNode = NearestTo(new FVector(at.X, at.Y, lip) + inward * 45f, 48f);
            if (bottom < 0 || topNode < 0 || bottom == lastBottom || !_nodes[bottom].Standing)
            {
                continue;
            }

            lastBottom = bottom;
            AddLink(new NavLink(NavEdgeKind.LedgeClimb, bottom, topNode, new FVector(approach.X, approach.Y, bz), yaw,
                _nodes[topNode].Floor, ge.Index, height), 4.2f * AiK.CrouchSpeed);
        }
    }

    private void AddPipe(GERecord ge)
    {
        ReadOnlySpan<FVector> p = ge.Points;
        FVector bottomP = p[0];
        FVector topP = p[1];
        var toClimber = new FVector(p[2].X - p[0].X, p[2].Y - p[0].Y, 0f);
        float len = toClimber.Size();
        float height = topP.Z - bottomP.Z;
        if (len < 0.5f || height < 100f)
        {
            return;
        }

        toClimber *= 1f / len;
        FVector approach = bottomP + toClimber * 100f;
        float? floorZ = FloorNear(approach, bottomP.Z + 60f, bottomP.Z - 60f);
        if (floorZ is not { } fz)
        {
            return;
        }

        int bottom = NearestTo(new FVector(approach.X, approach.Y, fz), 40f);
        int top = -1;
        foreach (float across in stackalloc float[] { 60f, 90f, 120f })
        {
            FVector exit = topP - toClimber * across;
            if (FloorNear(exit, topP.Z + 60f, topP.Z - 80f) is { } tz)
            {
                top = NearestTo(new FVector(exit.X, exit.Y, tz), 48f);
                if (top >= 0)
                {
                    break;
                }
            }
        }

        if (bottom < 0 || top < 0 || !_nodes[bottom].Standing)
        {
            return;
        }

        AddLink(new NavLink(NavEdgeKind.PipeClimb, bottom, top, new FVector(approach.X, approach.Y, fz), FaceYaw(-toClimber),
            _nodes[top].Floor, ge.Index, height), (height / AiK.PipeClimbSpeed + 3f) * AiK.CrouchSpeed);
    }

    /// <summary>
    /// A ladder (bottom, top, a point toward the climber): up from the grab spot 35 out on the climber's side to the
    /// floor beyond the top, and down from the floor 72 back from the top (walking toward the ladder) to that spot.
    /// </summary>
    private void AddLadder(GERecord ge)
    {
        ReadOnlySpan<FVector> p = ge.Points;
        if (p.Length < 3)
        {
            return;
        }

        FVector bottomP = p[0].Z <= p[1].Z ? p[0] : p[1];
        FVector topP = p[0].Z <= p[1].Z ? p[1] : p[0];
        var toClimber = new FVector(p[2].X - p[0].X, p[2].Y - p[0].Y, 0f);
        float len = toClimber.Size();
        float height = topP.Z - bottomP.Z;
        if (len < 0.5f || height < 64f)
        {
            return;
        }

        toClimber *= 1f / len;
        FVector grab = bottomP + toClimber * AiK.LadderGrabOut;
        if (FloorNear(grab, bottomP.Z + 60f, bottomP.Z - 60f) is not { } fz)
        {
            return;
        }

        int bottom = NearestTo(new FVector(grab.X, grab.Y, fz), 40f);
        if (bottom < 0 || !_nodes[bottom].Standing)
        {
            return;
        }

        var start = new FVector(grab.X, grab.Y, fz);
        int top = -1;
        foreach (float across in stackalloc float[] { 60f, 90f, 120f })
        {
            FVector exit = topP - toClimber * across;
            if (FloorNear(exit, topP.Z + 60f, topP.Z - 80f) is { } tz)
            {
                top = NearestTo(new FVector(exit.X, exit.Y, tz), 48f);
                if (top >= 0)
                {
                    break;
                }
            }
        }

        if (top < 0)
        {
            return;
        }

        float climb = (height / AiK.LadderSpeed + 4f) * AiK.CrouchSpeed;
        AddLink(new NavLink(NavEdgeKind.LadderUp, bottom, top, start, FaceYaw(-toClimber), _nodes[top].Floor, ge.Index, height), climb);

        // Down from the top: walk toward the ladder from 72 back; the feet test hangs the pawn on at the top.
        FVector back = topP - toClimber * AiK.LadderTopBack;
        if (FloorNear(back, topP.Z + 60f, topP.Z - 80f) is { } bz)
        {
            int from = NearestTo(new FVector(back.X, back.Y, bz), 40f);
            if (from >= 0 && _nodes[from].Standing)
            {
                AddLink(new NavLink(NavEdgeKind.LadderDown, from, bottom, new FVector(back.X, back.Y, bz), FaceYaw(toClimber), start,
                    ge.Index, height), climb + AiK.CrouchSpeed);
            }
        }
    }

    /// <summary>
    /// A horizontal pipe a standing jump catches: from the floor under it, 80 in from one end, along it to the far
    /// end and down to the floor there; one link each way.
    /// </summary>
    private void AddHandOverHand(GERecord ge)
    {
        FVector p0 = ge.P0;
        FVector p1 = ge.P1;
        var along = new FVector(p1.X - p0.X, p1.Y - p0.Y, 0f);
        float length = along.Size();
        if (length < 2f * AiK.HohGrabIn + 32f || MathF.Abs(p1.Z - p0.Z) > 16f)
        {
            return;
        }

        along *= 1f / length;
        float barZ = (p0.Z + p1.Z) * 0.5f;
        for (int way = 0; way < 2; way++)
        {
            FVector a = way == 0 ? p0 : p1;
            FVector d = way == 0 ? along : -along;
            FVector at = a + d * AiK.HohGrabIn;
            if (FloorNear(at, barZ - AiK.HohJumpMin + 20f, barZ - AiK.HohJumpMax) is not { } fz || barZ - fz < AiK.HohJumpMin ||
                FloorNear(at - d * AiK.HohRunIn, fz + 12f, fz - 12f) is null)
            {
                continue;
            }

            int from = NearestTo(new FVector(at.X, at.Y, fz), 24f);
            if (from < 0 || !_nodes[from].Standing)
            {
                continue;
            }

            // The far end: the hands stop 27 short of it, then the drop (the feet hang about 200 under the bar).
            FVector end = a + d * (length - AiK.HohEndStop - 8f);
            if (FloorNear(end, barZ - 150f, barZ - 200f - _profile.MaxDrop) is not { } ez)
            {
                continue;
            }

            int to = NearestTo(new FVector(end.X, end.Y, ez), 48f);
            if (to < 0)
            {
                continue;
            }

            float fall = MathF.Max(0f, barZ - 200f - ez);
            float cost = ((length - AiK.HohGrabIn) / AiK.HohSpeed + 4f) * AiK.CrouchSpeed + (fall > _profile.SafeDrop ? 600f : 0f);
            AddLink(new NavLink(NavEdgeKind.HandOverHand, from, to, new FVector(at.X, at.Y, fz), FaceYaw(d), _nodes[to].Floor, ge.Index,
                barZ - fz), cost);
        }
    }

    /// <summary>
    /// A fence under a FENCE line: from the floor on one side, facing it, over the top to the floor on the other side,
    /// at a few spots along it; both ways. The fence itself must stand under the line (a wall the body runs into).
    /// </summary>
    private void AddFence(GERecord ge)
    {
        FVector p0 = ge.P0;
        FVector p1 = ge.P1;
        var along = new FVector(p1.X - p0.X, p1.Y - p0.Y, 0f);
        float length = along.Size();
        float margin = 80f + _profile.Radius;
        if (length < 2f * margin || MathF.Abs(p1.Z - p0.Z) > 16f)
        {
            return;
        }

        along *= 1f / length;
        var side = new FVector(-along.Y, along.X, 0f);
        float topZ = (p0.Z + p1.Z) * 0.5f;
        float reach = _profile.Radius + 25f;
        int samples = Math.Clamp((int)((length - 2f * margin) / 256f) + 1, 1, 4);
        for (int i = 0; i < samples; i++)
        {
            float t = samples == 1 ? length * 0.5f : margin + (length - 2f * margin) * (i / (float)(samples - 1));
            FVector at = p0 + along * t;
            for (int way = 0; way < 2; way++)
            {
                FVector s = way == 0 ? side : -side;
                FVector near = at + s * reach;
                FVector far = at - s * reach;
                if (FloorNear(near, topZ - AiK.FenceMinHeight, topZ - AiK.FenceMaxHeight) is not { } nz ||
                    FloorNear(far, topZ - 60f, topZ - AiK.FenceMaxHeight) is not { } fz)
                {
                    continue;
                }

                // The fence: a standing body walking at it from the near side is stopped.
                var body = new FVector(near.X, near.Y, nz + Lift(_profile.StandingHeight));
                if (!_world.LineCheck(out _, null, body - s * (reach + 20f), body, Body(_profile.StandingHeight), WorldFlags))
                {
                    continue;
                }

                int from = NearestTo(new FVector(near.X, near.Y, nz), 40f);
                int to = NearestTo(new FVector(far.X, far.Y, fz), 48f);
                if (from < 0 || to < 0 || !_nodes[from].Standing)
                {
                    continue;
                }

                float cost = ((topZ - nz + topZ - fz) / AiK.FenceSpeed + 5f) * AiK.CrouchSpeed;
                AddLink(new NavLink(NavEdgeKind.FenceClimb, from, to, new FVector(near.X, near.Y, nz), FaceYaw(-s), _nodes[to].Floor, ge.Index,
                    topZ - nz), cost);
            }
        }
    }

    private void AddZip(GERecord ge)
    {
        FVector high = ge.P0.Z >= ge.P1.Z ? ge.P0 : ge.P1;
        FVector low = ge.P0.Z >= ge.P1.Z ? ge.P1 : ge.P0;
        var down = new FVector(low.X - high.X, low.Y - high.Y, 0f);
        float flat = down.Size();
        if (flat < 200f)
        {
            return;
        }

        down *= 1f / flat;
        float slope = (low.Z - high.Z) / flat;

        // The run-up: a floor under the line near the high end, 250 to 330 uu below it, with 80 uu more of it ahead.
        int start = -1;
        FVector startFloor = default;
        for (float t = 24f; t <= 220f && start < 0; t += 16f)
        {
            FVector q = high + down * t;
            float lineZ = high.Z + slope * t;
            if (FloorNear(q, lineZ - 230f, lineZ - 340f) is not { } fz || FloorNear(q + down * 80f, fz + 12f, fz - 12f) is null)
            {
                continue;
            }

            int node = NearestTo(new FVector(q.X, q.Y, fz), 32f);
            if (node >= 0 && _nodes[node].Standing)
            {
                start = node;
                startFloor = new FVector(q.X, q.Y, fz);
            }
        }

        if (start < 0)
        {
            return;
        }

        // The landing: the floor under the line some 60 uu before the low end, no deeper than a planned drop.
        int land = -1;
        foreach (float back in stackalloc float[] { 60f, 100f, 150f })
        {
            FVector q = low - down * back;
            float lineZ = low.Z - slope * back;
            float hang = lineZ - AiK.LedgeHangDrop - _profile.StandingHeight;
            if (FloorNear(q, hang + 40f, hang - _profile.MaxDrop) is { } lz)
            {
                land = NearestTo(new FVector(q.X, q.Y, lz), 48f);
                if (land >= 0)
                {
                    break;
                }
            }
        }

        if (land < 0)
        {
            return;
        }

        float rideLength = (high - low).Size();
        AddLink(new NavLink(NavEdgeKind.ZipLine, start, land, startFloor, FaceYaw(down), _nodes[land].Floor, ge.Index, high.Z - startFloor.Z),
            (rideLength / AiK.ZipSpeed + 3f) * AiK.CrouchSpeed);
    }

    private void AddLink(NavLink link, float cost)
    {
        int index = _links.Count;
        _links.Add(link);
        _edges[link.From].Add((link.To, link.Kind, cost, index));
    }

    private static int FaceYaw(FVector dir) => new FVector(dir.X, dir.Y, 0f).Rotation().Yaw & 0xFFFF;

    /// <summary>The floor height under <paramref name="p"/> between <paramref name="top"/> and <paramref name="bottom"/>, or null.</summary>
    private float? FloorNear(FVector p, float top, float bottom)
    {
        var start = new FVector(p.X, p.Y, top);
        var end = new FVector(p.X, p.Y, bottom);
        if (!_world.LineCheck(out FCheckResult hit, null, end, start, FloorProbe, WorldFlags) || hit.Normal.Z < _profile.MinFloorNormalZ)
        {
            return null;
        }

        return hit.Location.Z - FloorProbe.Z;
    }

    /// <summary>The node nearest <paramref name="p"/> within <paramref name="radius"/> horizontally and 40 uu in height, or -1.</summary>
    private int NearestTo(FVector p, float radius)
    {
        float s = _profile.Spacing;
        int cx = (int)MathF.Round((p.X - _min.X) / s);
        int cy = (int)MathF.Round((p.Y - _min.Y) / s);
        int cells = (int)MathF.Ceiling(radius / s);
        int best = -1;
        float bestD = float.MaxValue;
        for (int dx = -cells; dx <= cells; dx++)
        {
            for (int dy = -cells; dy <= cells; dy++)
            {
                if (!_columns.TryGetValue(Key(cx + dx, cy + dy), out List<int>? column))
                {
                    continue;
                }

                foreach (int n in column)
                {
                    FVector q = _nodes[n].Floor;
                    float d2 = (q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y);
                    if (d2 <= radius * radius && MathF.Abs(q.Z - p.Z) <= 40f && d2 < bestD)
                    {
                        bestD = d2;
                        best = n;
                    }
                }
            }
        }

        return best;
    }

    private NavGraph Finish()
    {
        var starts = new int[_nodes.Count + 1];
        int total = 0;
        for (int n = 0; n < _nodes.Count; n++)
        {
            starts[n] = total;
            total += _edges[n].Count;
        }

        starts[_nodes.Count] = total;
        var to = new int[total];
        var kinds = new NavEdgeKind[total];
        var costs = new float[total];
        var links = new int[total];
        int e = 0;
        for (int n = 0; n < _nodes.Count; n++)
        {
            foreach ((int To, NavEdgeKind Kind, float Cost, int Link) edge in _edges[n])
            {
                to[e] = edge.To;
                kinds[e] = edge.Kind;
                costs[e] = edge.Cost;
                links[e] = edge.Link;
                e++;
            }
        }

        return new NavGraph(_profile, _min, _nodes.ToArray(), starts, to, kinds, costs, links, _links.ToArray());
    }
}
