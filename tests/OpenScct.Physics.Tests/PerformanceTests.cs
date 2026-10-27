using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

public sealed class PerformanceTests
{
    private static readonly FVector Pawn = new(35f, 35f, 87.5f);
    private readonly ITestOutputHelper _out;

    public PerformanceTests(ITestOutputHelper output) => _out = output;

    private static bool Optimized =>
        typeof(CollisionWorld).Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled != true;

    /// <summary>
    /// A synthetic map about the size of SteeE's collision (SteeE draws about 120k triangles): a tiled floor,
    /// buildings, clutter boxes, scattered triangles and hulls over 6400 x 6400 uu.
    /// </summary>
    private static CollisionData SteeESized(out int triangles)
    {
        var r = new Random(1234);
        var b = new CollisionDataBuilder();
        const float half = 3200f;
        const int tiles = 100;
        float cell = 2f * half / tiles;
        for (int i = 0; i < tiles; i++)
        {
            for (int j = 0; j < tiles; j++)
            {
                float x = -half + i * cell;
                float y = -half + j * cell;
                float z = (float)(r.NextDouble() * 4.0);
                b.AddQuad(V(x, y, z), V(x, y + cell, z), V(x + cell, y + cell, z), V(x + cell, y, z));
            }
        }

        for (int i = 0; i < 1500; i++)
        {
            FVector c = V((float)(r.NextDouble() * 2 - 1) * half, (float)(r.NextDouble() * 2 - 1) * half, 0f);
            FVector h = V(32f + (float)r.NextDouble() * 200f, 32f + (float)r.NextDouble() * 200f, 100f + (float)r.NextDouble() * 700f);
            b.AddBox(V(c.X - h.X, c.Y - h.Y, 0f), V(c.X + h.X, c.Y + h.Y, h.Z));
        }

        for (int i = 0; i < 6000; i++)
        {
            FVector c = V((float)(r.NextDouble() * 2 - 1) * half, (float)(r.NextDouble() * 2 - 1) * half, (float)r.NextDouble() * 300f);
            FVector h = V(5f + (float)r.NextDouble() * 40f, 5f + (float)r.NextDouble() * 40f, 5f + (float)r.NextDouble() * 40f);
            b.AddBox(c - h, c + h);
        }

        for (int i = 0; i < 25000; i++)
        {
            FVector c = V((float)(r.NextDouble() * 2 - 1) * half, (float)(r.NextDouble() * 2 - 1) * half, (float)r.NextDouble() * 600f);
            b.AddTriangle(c, c + V((float)r.NextDouble() * 60f, (float)r.NextDouble() * 60f, (float)r.NextDouble() * 60f),
                c + V(-(float)r.NextDouble() * 60f, (float)r.NextDouble() * 60f, (float)r.NextDouble() * 60f));
        }

        for (int i = 0; i < 400; i++)
        {
            FVector h = V(10f + (float)r.NextDouble() * 60f, 10f + (float)r.NextDouble() * 60f, 10f + (float)r.NextDouble() * 60f);
            b.AddBoxHullCollider(ColliderKind.MeshHull, null, CollisionDataBuilder.WorldFlags, -h, h,
                V((float)(r.NextDouble() * 2 - 1) * half, (float)(r.NextDouble() * 2 - 1) * half, (float)r.NextDouble() * 300f),
                new FRotator(0, r.Next(65536), 0));
        }

        CollisionData data = b.Build();
        triangles = data.Triangles.Length;
        return data;
    }

    [Fact]
    public void ThousandBoxSweepsOnASteeESizedMap()
    {
        var watch = Stopwatch.StartNew();
        CollisionData data = SteeESized(out int triangles);
        long buildData = watch.ElapsedMilliseconds;
        watch.Restart();
        CollisionWorld w = CollisionWorld.Build(data);
        long buildWorld = watch.ElapsedMilliseconds;

        // Pawn moves: mostly short steps at pawn height, some long runs and drops.
        var r = new Random(99);
        var starts = new FVector[1000];
        var ends = new FVector[1000];
        for (int i = 0; i < starts.Length; i++)
        {
            starts[i] = V((float)(r.NextDouble() * 2 - 1) * 3000f, (float)(r.NextDouble() * 2 - 1) * 3000f, 90f + (float)r.NextDouble() * 400f);
            double a = r.NextDouble() * Math.PI * 2.0;
            float len = i % 10 < 7 ? 5f + (float)r.NextDouble() * 45f : 200f + (float)r.NextDouble() * 1000f;
            float dz = i % 10 == 9 ? -400f : 0f;
            ends[i] = starts[i] + V((float)Math.Cos(a) * len, (float)Math.Sin(a) * len, dz);
        }

        int hits = 0;
        double best = double.MaxValue;
        for (int run = 0; run < 7; run++)
        {
            hits = 0;
            watch.Restart();
            for (int i = 0; i < starts.Length; i++)
            {
                if (w.LineCheck(out _, null, ends[i], starts[i], Pawn, ETraceFlags.TRACE_World))
                {
                    hits++;
                }
            }

            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        _out.WriteLine($"{triangles} triangles + {w.HullCount} hulls; data {buildData} ms, BVH build {buildWorld} ms; " +
                       $"1000 box sweeps: best {best:F2} ms ({hits} hit), optimized build: {Optimized}");
        Assert.True(triangles >= 120000);
        // Shared CI runners vary several times over between runs, so the time budget is only enforced on a desktop
        // (the timing above is still printed on CI).
        double budget = Optimized ? 20.0 : 80.0;
        if (Environment.GetEnvironmentVariable("CI") != "true")
        {
            Assert.True(best < budget, $"1000 sweeps took {best:F2} ms (budget {budget:F0} ms)");
        }
    }

    [Fact]
    public void SteeEDropGrid()
    {
        string root = TestPaths.SteeE;
        if (!Directory.Exists(root))
        {
            Assert.Skip($"{root} is not built (local only).");
        }

        var watch = Stopwatch.StartNew();
        SvMap map = SvMap.Load(root);
        long load = watch.ElapsedMilliseconds;
        watch.Restart();
        CollisionWorld w = CollisionWorld.Build(map.Collision);
        long build = watch.ElapsedMilliseconds;

        const int n = 20;
        FVector min = w.WorldMin;
        FVector max = w.WorldMax;
        int floors = 0;
        int anyHit = 0;
        int steep = 0;
        float topZ = max.Z + 200f;
        float bottomZ = min.Z - 200f;
        watch.Restart();
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                float x = min.X + (max.X - min.X) * (i + 0.5f) / n;
                float y = min.Y + (max.Y - min.Y) * (j + 0.5f) / n;
                if (w.LineCheck(out FCheckResult hit, null, V(x, y, bottomZ), V(x, y, topZ), Pawn, ETraceFlags.TRACE_World))
                {
                    anyHit++;
                    if (hit.Normal.Z >= K.MINFLOORZ)
                    {
                        floors++;
                    }
                    else
                    {
                        steep++;
                    }
                }
            }
        }

        double drops = watch.Elapsed.TotalMilliseconds;
        _out.WriteLine($"SteeE: {w.TriangleCount} triangles, {w.HullCount} hulls, {w.ColliderCount} colliders, " +
                       $"{w.DegenerateTriangles} degenerate; load {load} ms, BVH build {build} ms");
        _out.WriteLine($"400 drops from z {topZ:F0}: {anyHit} hit ({floors} walkable floors, {steep} steep), {drops:F2} ms");
        Assert.True(anyHit > 0);

        // Pawn moves from the floors found: mostly one-tick steps, some long runs.
        var spots = new System.Collections.Generic.List<FVector>();
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                float x = min.X + (max.X - min.X) * (i + 0.5f) / n;
                float y = min.Y + (max.Y - min.Y) * (j + 0.5f) / n;
                if (w.LineCheck(out FCheckResult hit, null, V(x, y, bottomZ), V(x, y, topZ), Pawn, ETraceFlags.TRACE_World) &&
                    hit.Normal.Z >= K.MINFLOORZ)
                {
                    spots.Add(hit.Location);
                }
            }
        }

        var r = new Random(5);
        var starts = new FVector[1000];
        var ends = new FVector[1000];
        for (int i = 0; i < starts.Length; i++)
        {
            starts[i] = spots[r.Next(spots.Count)];
            double a = r.NextDouble() * Math.PI * 2.0;
            float len = i % 10 < 7 ? 5f + (float)r.NextDouble() * 45f : 200f + (float)r.NextDouble() * 1000f;
            ends[i] = starts[i] + V((float)Math.Cos(a) * len, (float)Math.Sin(a) * len, 0f);
        }

        double best = double.MaxValue;
        int blocked = 0;
        for (int run = 0; run < 5; run++)
        {
            blocked = 0;
            watch.Restart();
            for (int i = 0; i < starts.Length; i++)
            {
                if (w.LineCheck(out _, null, ends[i], starts[i], Pawn, ETraceFlags.TRACE_World))
                {
                    blocked++;
                }
            }

            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        _out.WriteLine($"1000 pawn sweeps from {spots.Count} floor spots: best {best:F2} ms, {blocked} blocked, optimized build: {Optimized}");

        _out.WriteLine(SteeESoak(map, r));
    }

    /// <summary>
    /// Pawns spawned at SteeE's PlayerStarts (inside the level; the drops above land on the outside of the level
    /// shell, which is solid in UE2) walking at random for 20 s with the map's actors loaded: none may end inside
    /// geometry or fall out of the world.
    /// </summary>
    private static string SteeESoak(SvMap map, Random r)
    {
        var world = new World(9);
        Content.Loading.MapLoadResult loaded = new Content.Loading.MapLoader().Load(world, map);
        PhysicsWorld physics = PhysicsWorld.Attach(map, loaded);
        CollisionWorld w = physics.Collision;
        var pawns = new System.Collections.Generic.List<TestPawn>();
        foreach (Content.ActorRecord record in map.Gameplay.AllActors)
        {
            if (record.Class == "Engine.PlayerStart" && pawns.Count < 8 && record.Location is { } at)
            {
                // PlayerStarts are 80 high and the spy is 87.5: spawning nudges the pawn clear, as UE2 does.
                TestPawn p = world.Level.SpawnActor(new TestPawn(), at, default)!;
                FVector spot = at;
                Assert.True(physics.Driver.FindSpot(p, ref spot), $"no room at {record.Id}");
                p.Location = spot;
                p.Physics = Core.Engine.EPhysics.PHYS_Falling;
                var c = world.Level.SpawnActor(new TestController(), spot, default)!;
                c.Pawn = p;
                p.Controller = c;
                pawns.Add(p);
            }
        }

        w.NotifyCollisionChanged();
        int inside = 0;
        var firstInside = new System.Collections.Generic.List<string>();
        int fallen = 0;
        int walking = 0;
        var watch = Stopwatch.StartNew();
        for (int t = 0; t < 20 * 60; t++)
        {
            if (t % 45 == 0)
            {
                foreach (TestPawn p in pawns)
                {
                    double a = r.NextDouble() * Math.PI * 2.0;
                    p.Acceleration = V((float)Math.Cos(a) * 3000f, (float)Math.Sin(a) * 3000f, 0f);
                }
            }

            world.Step();
            foreach (TestPawn p in pawns)
            {
                if (p.Location.Z < w.KillZ)
                {
                    fallen++;
                }
                // The shape the movement collides with: the box against BSP, the cylinder against static meshes
                // (UStaticMesh::LineCheck sweeps a cylinder, so the box's corners may legitimately sit inside a mesh).
                else if (w.OverlapMoveShape(out FCheckResult inHit, p, p.Location, V(34.9f, 34.9f, 87.4f), Movement.PhysicsDriver.MoveTraceFlags(p)))
                {
                    if (inside++ < 5)
                    {
                        firstInside.Add($"t{t} {p.Location} {(inHit.Collider >= 0 ? w.GetCollider(inHit.Collider).Kind.ToString() : inHit.Actor?.GetType().Name)} {inHit.Collider} n {inHit.Normal}");
                    }
                }

                if (p.Physics == Core.Engine.EPhysics.PHYS_Walking)
                {
                    walking++;
                }
            }
        }

        string report = $"SteeE soak: {pawns.Count} pawns x 1200 ticks in {watch.ElapsedMilliseconds} ms; {walking} walking, " +
                        $"{inside} inside geometry, {fallen} below KillZ";
        Assert.True(pawns.Count > 0);
        Assert.True(inside == 0, report + "; first: " + string.Join(" | ", firstInside));
        Assert.Equal(0, fallen);
        return report;
    }
}
