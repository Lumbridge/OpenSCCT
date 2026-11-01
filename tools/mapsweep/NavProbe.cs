using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using OpenScct.Bots;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Physics.Collision;
using OpenScct.Server;
using PhysK = OpenScct.Physics.K;

namespace OpenScct.MapSweep;

/// <summary>
/// <c>--probe=Map:x,y,z[:merc]</c>: draws the walk graph around a point as text, one character per grid column, so a
/// start island or an unreachable objective can be looked at. <c>S</c> the point, <c>x</c> a node reached from the
/// point's node, <c>o</c> a node not reached, <c>-</c> a node far above or below, blank no node. Then the world
/// collision answers a pawn's box at the point and along the four axes.
/// </summary>
internal static class NavProbe
{
    public static int Run(string arg)
    {
        string[] parts = arg.Split(':');
        string map = parts[0];
        string[] xyz = parts[1].Split(',');
        var p = new FVector(float.Parse(xyz[0], CultureInfo.InvariantCulture), float.Parse(xyz[1], CultureInfo.InvariantCulture),
            float.Parse(xyz[2], CultureInfo.InvariantCulture));
        bool merc = parts.Length > 2 && parts[2] == "merc";
        int half = parts.Length > 3 ? int.Parse(parts[3], CultureInfo.InvariantCulture) : 24;
        var world = new World(0, ENetMode.NM_DedicatedServer);
        MapCatalog maps = MapCatalog.CreateDefault();
        if (!maps.TryLoad(world, map, false, out MapInfo? info, out string? error) || info?.Physics is null)
        {
            Console.Error.WriteLine("map did not load: " + error);
            return 1;
        }

        NavGraph g = new AiNavCache().Get(info, spy: !merc, world.Level.Info.GEActivationMask);
        float height = merc ? 88f : 88f;
        int start = g.NearestNode(p, height, 400f);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{map} {(merc ? "merc" : "spy")} graph {g.NodeCount} nodes, node at {MapSweeper.Fmt(p)}: {start}"));
        float[] costs = start >= 0 ? g.CostsFrom(start) : [];
        int reached = 0;
        FVector reachMin = p, reachMax = p;
        for (int n = 0; n < costs.Length; n++)
        {
            if (costs[n] >= float.MaxValue)
            {
                continue;
            }
            reached++;
            FVector f = g.Node(n).Floor;
            reachMin = new FVector(MathF.Min(reachMin.X, f.X), MathF.Min(reachMin.Y, f.Y), MathF.Min(reachMin.Z, f.Z));
            reachMax = new FVector(MathF.Max(reachMax.X, f.X), MathF.Max(reachMax.Y, f.Y), MathF.Max(reachMax.Z, f.Z));
        }
        Console.WriteLine($"reachable {reached} bounds {MapSweeper.Fmt(reachMin)} - {MapSweeper.Fmt(reachMax)}");
        foreach (var terminal in world.Level.AllActors<OpenScct.Game.SBase.SComputerObjectiveTrigger>())
        {
            Pawn pawn = merc ? new OpenScct.Game.SBase.SPAWNDEFENSE() : new OpenScct.Game.SBase.SPawnAttaque();
            terminal.GetUseSpot(pawn, out FVector spot, out _);
            int target = g.NearestNode(new FVector(spot.X, spot.Y, terminal.Location.Z));
            if (target >= 0)
            {
                FVector floor = g.Node(target).Floor;
                var candidates = new List<int>();
                g.NodesNear(floor, 300f, candidates);
                int reachablePosts = candidates.FindAll(n => costs.Length > n && costs[n] < float.MaxValue && g.Node(n).Standing).Count;
                Console.WriteLine($"terminal {terminal.TriggerName} at {MapSweeper.Fmt(terminal.Location)} use floor {MapSweeper.Fmt(floor)} reachable {costs.Length > target && costs[target] < float.MaxValue} nearby reachable standing nodes {reachablePosts}");
            }
        }
        float spacing = g.Profile.Spacing;
        var sb = new StringBuilder();
        var near = new List<int>();
        for (int iy = half; iy >= -half; iy--)
        {
            for (int ix = -half; ix <= half; ix++)
            {
                var c = new FVector(p.X + ix * spacing, p.Y + iy * spacing, p.Z);
                char ch = ' ';
                g.NodesNear(c, spacing * 0.5f, near);
                foreach (int n in near)
                {
                    float dz = g.Node(n).Floor.Z - (p.Z - height);
                    char here = Math.Abs(dz) > 150f ? '-' : costs.Length > 0 && costs[n] < float.MaxValue ? 'x' : 'o';
                    if (here == 'x' || (here == 'o' && ch is ' ' or '-') || (here == '-' && ch == ' '))
                    {
                        ch = here;
                    }
                }

                if (ix == 0 && iy == 0)
                {
                    ch = 'S';
                }

                sb.Append(ch);
            }

            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  y {p.Y + iy * spacing:F0}"));
        }

        Console.Write(sb.ToString());
        if (start >= 0)
        {
            NavNode node = g.Node(start);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"start node floor {MapSweeper.Fmt(node.Floor)} standing {node.Standing}"));
            foreach ((int to, NavEdgeKind kind, float cost, int link) in g.EdgesOf(start))
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  edge to {to} {MapSweeper.Fmt(g.Node(to).Floor)} {kind} cost {cost:F0} link {link}"));
            }
        }

        CollisionWorld col = info.Physics.Collision;
        for (float dy = -320f; dy <= 640f; dy += 32f)
        {
            FVector top = p + new FVector(0f, dy, 200f);
            var line = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"column y {p.Y + dy:F0}:"));
            for (int k = 0; k < 6; k++)
            {
                if (!col.LineCheck(out FCheckResult f, null, top - new FVector(0f, 0f, 1200f), top, new FVector(4f, 4f, 2f), PhysK.Flags.MoveWorld))
                {
                    break;
                }

                line.Append(string.Create(CultureInfo.InvariantCulture, $" z {f.Location.Z - 2f:F0} n {f.Normal.Z:F2} c {f.Collider} {f.Actor}"));
                top = new FVector(top.X, top.Y, f.Location.Z - 4f);
            }

            if (col.LineCheck(out FCheckResult ff, null, p + new FVector(0f, dy, -1000f), p + new FVector(0f, dy, 200f), new FVector(4f, 4f, 2f), PhysK.Flags.MoveWorld))
            {
                // The walk graph's fit test: the crouched body box from a quarter step over the floor.
                float h = merc ? 68f : 50f;
                var body = new FVector(g.Profile.Radius, g.Profile.Radius, h - g.Profile.StepUp * 0.25f);
                FVector at = new FVector(p.X, p.Y + dy, ff.Location.Z - 2f + h + g.Profile.StepUp * 0.25f + 2f);
                bool pc = col.PointCheck(out FCheckResult ph, null, at, body, ETraceFlags.TRACE_Blocking | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry | ETraceFlags.TRACE_Volumes | ETraceFlags.TRACE_Others);
                bool sw = col.LineCheck(out FCheckResult sh, null, at - new FVector(0f, 0f, 0.5f), at + new FVector(0f, 0f, 0.5f), body, PhysK.Flags.MoveWorld) && sh.bStartPenetrating;
                line.Append(string.Create(CultureInfo.InvariantCulture, $" | fit point check {(pc ? "blocked c" + ph.Collider.ToString(CultureInfo.InvariantCulture) : "fits")} move shape {(sw ? "blocked" : "fits")}"));
            }

            Console.WriteLine(line.ToString());
        }

        var extent = new FVector(35f, 35f, height);
        bool overlaps = col.PointCheck(out FCheckResult hit, null, p, extent, PhysK.Flags.MoveWorld);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"point check at {MapSweeper.Fmt(p)}: {(overlaps ? "overlaps collider " + hit.Collider.ToString(CultureInfo.InvariantCulture) + " " + (hit.Actor?.ToString() ?? "") : "clear")}"));
        foreach (float up in new[] { 64f, 400f })
        {
            bool roof = col.LineCheck(out FCheckResult r, null, p + new FVector(0f, 0f, up), p, new FVector(35f, 35f, 87.5f), (ETraceFlags)0x2df);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  box (35, 35, 87.5) up {up:F0}: {(roof ? $"hit at {r.Time * up:F1} uu collider {r.Collider} {r.Actor} normal {MapSweeper.Fmt(r.Normal * 100f)} at {MapSweeper.Fmt(r.Location)} start inside {r.bStartPenetrating}" : "clear")}"));
        }

        foreach (FVector dir in new[] { new FVector(1f, 0f, 0f), new FVector(-1f, 0f, 0f), new FVector(0f, 1f, 0f), new FVector(0f, -1f, 0f) })
        {
            bool blocked = col.LineCheck(out FCheckResult h, null, p + dir * 400f, p, extent, PhysK.Flags.MoveWorld);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  sweep {MapSweeper.Fmt(dir)} 400: {(blocked ? $"hit at {h.Time * 400f:F0} uu collider {h.Collider} {h.Actor} normal {MapSweeper.Fmt(h.Normal * 100f)} start inside {h.bStartPenetrating}" : "clear")}"));
        }

        if (parts.Length > 4)
        {
            OpenScct.Content.SvMap svmap = OpenScct.Content.SvMap.Load(maps.FindFolder(map)!);
            var cd = svmap.Collision;
            for (int ci = 0; ci < cd.Colliders.Length; ci++)
            {
                OpenScct.Content.Collision.Collider c = cd.Colliders[ci];
                if (c.AabbMax.X < p.X - 300f || c.AabbMin.X > p.X + 300f || c.AabbMax.Y < p.Y - 300f || c.AabbMin.Y > p.Y + 300f ||
                    c.AabbMax.Z < p.Z - 400f || c.AabbMin.Z > p.Z + 400f || ci == 0)
                {
                    continue;
                }

                string normals = string.Empty;
                foreach (var tri in cd.TrianglesOf(c))
                {
                    _ = tri;
                }

                for (int ti = (int)c.TriStart; ti < c.TriStart + Math.Min(c.TriCount, 4u); ti++)
                {
                    normals += " " + MapSweeper.Fmt(cd.TriangleNormal(ti) * 100f);
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"near collider {ci}: {c.Kind} actor '{cd.ActorOf(c)}' tris {c.TriCount} aabb {MapSweeper.Fmt(c.AabbMin)} - {MapSweeper.Fmt(c.AabbMax)} normals{normals}"));
            }

            foreach (string id in parts[4].Split(','))
            {
                int ci = int.Parse(id, CultureInfo.InvariantCulture);
                OpenScct.Content.Collision.Collider c = svmap.Collision.Colliders[ci];
                foreach (FVector v in svmap.Collision.VerticesOf(c))
                {
                    Console.WriteLine("  vertex " + MapSweeper.Fmt(v));
                }

                foreach (OpenScct.Content.Collision.CollisionTriangle tri in svmap.Collision.TrianglesOf(c))
                {
                    Console.WriteLine("  tri " + tri.ToString());
                }

                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"collider {ci}: {c.Kind} {c.Flags} actor {svmap.Collision.ActorOf(c)} geometry {c.Geometry} tris {c.TriCount} hulls {c.HullCount} loc {MapSweeper.Fmt(c.Location)} rot ({c.Rotation.Pitch}, {c.Rotation.Yaw}, {c.Rotation.Roll}) scale ({c.Scale.X:F2}, {c.Scale.Y:F2}, {c.Scale.Z:F2}) prepivot {MapSweeper.Fmt(c.PrePivot)} aabb {MapSweeper.Fmt(c.AabbMin)} - {MapSweeper.Fmt(c.AabbMax)}"));
            }
        }

        return 0;
    }
}
