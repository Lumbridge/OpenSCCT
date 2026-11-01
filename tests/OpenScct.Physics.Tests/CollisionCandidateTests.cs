using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

public sealed class CollisionCandidateTests(ITestOutputHelper output)
{
    private sealed class CandidateActor : Actor { }

    private static void Same(bool expectedHit, in FCheckResult expected, bool actualHit, in FCheckResult actual)
    {
        Assert.Equal(expectedHit, actualHit);
        Assert.Equal(expected.Time, actual.Time);
        Assert.Equal(expected.Location, actual.Location);
        Assert.Equal(expected.Normal, actual.Normal);
        Assert.Equal(expected.Collider, actual.Collider);
        Assert.Equal(expected.Item, actual.Item);
        Assert.Equal(expected.Material, actual.Material);
        Assert.Equal(expected.Actor, actual.Actor);
        Assert.Equal(expected.bStartPenetrating, actual.bStartPenetrating);
    }

    private static FVector RandomPoint(Random random, float radius) => new(
        ((float)random.NextDouble() * 2f - 1f) * radius,
        ((float)random.NextDouble() * 2f - 1f) * radius,
        ((float)random.NextDouble() * 2f - 1f) * radius);

    private static void EagerSync(CollisionWorld collision)
    {
        for (int i = 0; i < collision.ColliderCount; i++) collision.SyncCollider(collision.ColliderAt(i));
    }

    [Fact]
    public void MovedMeshesMatchEagerSyncAndBruteForceIncludingTeleportsAndRotations()
    {
        var random = new Random(83819);
        var world = new World(12);
        var builder = new CollisionDataBuilder();
        var owners = new Dictionary<string, Actor>();
        var actors = new List<Actor>();
        for (int i = 0; i < 48; i++)
        {
            string id = "mesh" + i;
            Actor actor = world.Level.SpawnActor(new CandidateActor(), RandomPoint(random, 1000f), default)!;
            actors.Add(actor);
            owners.Add(id, actor);
            FVector half = V(20f + i, 30f + i, 45f + i);
            FVector scale = V(i % 3 == 0 ? -1.7f : 0.8f, 1.4f, 0.6f);
            if (i % 2 == 0)
                builder.AddBoxHullCollider(ColliderKind.Mover, id, CollisionDataBuilder.WorldFlags, -half, half,
                    actor.Location, writeCorners: i % 4 == 0, scale: scale);
            else
                builder.AddBoxMeshCollider(ColliderKind.Gpo, id, CollisionDataBuilder.WorldFlags, [(-half, half)],
                    actor.Location, scale: scale, prePivot: V(30f, -15f, 10f));
        }

        CollisionWorld collision = CollisionWorld.Build(builder.Build());
        collision.BindLevel(world.Level, id => owners.GetValueOrDefault(id));
        var actualMulti = new List<FCheckResult>();
        var expectedMulti = new List<FCheckResult>();
        int hitCount = 0;
        int deferredRefits = 0, eligibleRefits = 0;
        for (int q = 0; q < 300; q++)
        {
            // No world tick between any of these queries: cache invalidation must follow live transforms.
            foreach (Actor actor in actors)
            {
                actor.Location = RandomPoint(random, q % 3 == 0 ? 300f : 2000f);
                actor.Rotation = new FRotator(random.Next(65536), random.Next(65536), random.Next(65536));
            }

            FVector center = actors[q % actors.Count].Location;
            FVector start = center + V(-350f, 0f, 0f);
            FVector end = center + V(350f, 0f, 0f);
            FVector extent = q % 3 == 0 ? default : V(35f, 35f, q % 2 == 0 ? 50f : 87.5f);
            ETraceFlags flags = ETraceFlags.TRACE_AllColliding;
            if (q % 7 == 0) flags |= ETraceFlags.TRACE_StopAtFirstHit;
            collision.SetColliderEnabled(q % actors.Count + 1, q % 5 != 0);
            bool actualHit = collision.LineCheck(out FCheckResult actual, null, end, start, extent, flags);
            if (actualHit) hitCount++;
            if ((flags & ETraceFlags.TRACE_StopAtFirstHit) == 0)
            {
                for (int i = 1; i < collision.ColliderCount; i++)
                {
                    ColliderState state = collision.ColliderAt(i);
                    if (!state.Enabled) continue;
                    eligibleRefits++;
                    if (state.Location != state.Actor!.Location || state.Rotation != state.Actor.Rotation) deferredRefits++;
                }
            }
            EagerSync(collision);
            bool expectedHit = collision.LineCheck(out FCheckResult expected, null, end, start, extent, flags);
            Same(expectedHit, expected, actualHit, actual);

            // Restore an earlier pose as a rewind would, then test overlap against the all-primitives oracle.
            actors[q % actors.Count].Location -= V(8f, 0f, 0f);
            actualHit = collision.PointCheck(out actual, null, center, extent, ETraceFlags.TRACE_AllColliding);
            expectedHit = collision.Overlap(out expected, null, null, -1, center, extent, ETraceFlags.TRACE_AllColliding, bruteForce: true);
            Same(expectedHit, expected, actualHit, actual);

            actors[(q + 1) % actors.Count].Location = center;
            collision.MultiLineCheck(actualMulti, null, end, start, extent, ETraceFlags.TRACE_AllColliding);
            EagerSync(collision);
            collision.MultiLineCheck(expectedMulti, null, end, start, extent, ETraceFlags.TRACE_AllColliding);
            Assert.Equal(expectedMulti.Count, actualMulti.Count);
            for (int i = 0; i < expectedMulti.Count; i++) Same(true, expectedMulti[i], true, actualMulti[i]);
        }

        Assert.True(hitCount > 100, $"Expected substantial hit coverage, got {hitCount}.");
        Assert.True(deferredRefits > eligibleRefits / 2, $"Only deferred {deferredRefits}/{eligibleRefits} distant refits.");
        output.WriteLine($"Differential: 300 sweeps, 300 overlaps, 300 multi-hit queries; {hitCount} sweep hits; avoided {deferredRefits}/{eligibleRefits} eligible mesh refits on full-order sweeps.");
    }

    [Fact]
    public void DistantMeshDefersRefitButAnImmediateTeleportStillHits()
    {
        var world = new World(1);
        Actor mover = world.Level.SpawnActor(new CandidateActor(), V(10000f, 0f, 0f), default)!;
        var builder = new CollisionDataBuilder();
        int index = builder.AddBoxMeshCollider(ColliderKind.Mover, "mover", CollisionDataBuilder.WorldFlags,
            [(V(-40f, -50f, -60f), V(40f, 50f, 60f))], mover.Location, scale: V(-2f, 0.5f, 1.5f), prePivot: V(10f, 0f, 0f));
        CollisionWorld collision = CollisionWorld.Build(builder.Build());
        collision.BindLevel(world.Level, _ => mover);
        mover.Location = V(20000f, 0f, 0f);
        mover.Rotation = new FRotator(0, 8192, 0);
        Assert.False(collision.LineCheck(out _, null, V(200f, 0f, 0f), V(-200f, 0f, 0f), default, ETraceFlags.TRACE_AllColliding));
        Assert.NotEqual(mover.Location, collision.ColliderAt(index).Location);
        mover.Location = default;
        Assert.True(collision.LineCheck(out FCheckResult hit, null, V(200f, 0f, 0f), V(-200f, 0f, 0f), default, ETraceFlags.TRACE_AllColliding));
        Assert.Equal(index, hit.Collider);
        Assert.Equal(mover.Location, collision.GetCollider(index).Location);
        mover.Location = V(30000f, 0f, 0f);
        Assert.Equal(mover.Location, collision.GetCollider(index).Location);
    }

    [Fact]
    public void ActorOverlapDefersDistantRefitsButFollowsSameTickTeleportRollbackAndDisable()
    {
        var world = new World(17);
        Actor mover = world.Level.SpawnActor(new CandidateActor(), V(10000f, 0f, 0f), default)!;
        var builder = new CollisionDataBuilder();
        int index = builder.AddBoxMeshCollider(ColliderKind.Mover, "mover", CollisionDataBuilder.WorldFlags,
            [(V(-40f, -50f, -60f), V(40f, 50f, 60f))], mover.Location,
            scale: V(-2f, 0.5f, 1.5f), prePivot: V(10f, 0f, 0f));
        CollisionWorld collision = CollisionWorld.Build(builder.Build());
        collision.BindLevel(world.Level, _ => mover);
        FVector original = mover.Location;
        FVector extent = V(250f, 250f, 250f);
        mover.Location = V(20000f, 0f, 0f);
        mover.Rotation = new FRotator(0, 8192, 0);
        Assert.False(collision.OverlapsActorColliders(mover, default, extent));
        Assert.Equal(original, collision.ColliderAt(index).Location);
        Assert.Equal(default, collision.ColliderAt(index).Rotation);

        // Encroachment temporarily tests a pose then restores it; no world tick separates these queries.
        mover.Location = default;
        Assert.True(collision.OverlapsActorColliders(mover, default, extent));
        Assert.Equal(mover.Rotation, collision.ColliderAt(index).Rotation);
        mover.Location = original;
        mover.Rotation = default;
        Assert.False(collision.OverlapsActorColliders(mover, default, extent));
        Assert.Equal(default, collision.ColliderAt(index).Location);
        Assert.True(collision.OverlapsActorColliders(mover, original, extent));
        Assert.Equal(original, collision.ColliderAt(index).Location);
        Assert.Equal(default, collision.ColliderAt(index).Rotation);

        collision.SetColliderEnabled(index, false);
        mover.Location = default;
        Assert.False(collision.OverlapsActorColliders(mover, default, extent));
        Assert.Equal(original, collision.ColliderAt(index).Location);
        collision.SetColliderEnabled(index, true);
        Assert.True(collision.OverlapsActorColliders(mover, default, extent));
    }

    [Fact]
    public void ActorOverlapsMatchEagerRefitsForMovedScaledMeshesAndHulls()
    {
        var random = new Random(29117);
        var world = new World(18);
        var builder = new CollisionDataBuilder();
        var owners = new Dictionary<string, Actor>();
        var entries = new List<(Actor Actor, int Collider)>();
        for (int i = 0; i < 12; i++)
        {
            string id = "mover" + i;
            Actor actor = world.Level.SpawnActor(new CandidateActor(), V(10000f, 0f, 0f), default)!;
            owners.Add(id, actor);
            FVector half = V(30f + i, 45f + i, 70f + i);
            FVector scale = V(i % 3 == 0 ? -1.7f : 0.8f, 1.4f, 0.6f);
            int index = i % 2 == 0
                ? builder.AddBoxHullCollider(ColliderKind.Mover, id, CollisionDataBuilder.WorldFlags, -half, half,
                    actor.Location, writeCorners: i % 4 == 0, scale: scale)
                : builder.AddBoxMeshCollider(ColliderKind.Mover, id, CollisionDataBuilder.WorldFlags, [(-half, half)],
                    actor.Location, scale: scale, prePivot: V(30f, -15f, 10f));
            entries.Add((actor, index));
        }

        CollisionWorld collision = CollisionWorld.Build(builder.Build());
        collision.BindLevel(world.Level, id => owners.GetValueOrDefault(id));
        int hits = 0, deferred = 0;
        for (int pose = 0; pose < 40; pose++)
        {
            foreach ((Actor actor, int index) in entries)
            {
                actor.Location = RandomPoint(random, pose % 3 == 0 ? 150f : 2000f);
                actor.Rotation = new FRotator(random.Next(65536), random.Next(65536), random.Next(65536));
                FVector location = pose % 2 == 0 ? actor.Location : default;
                FVector extent = pose % 4 == 0 ? V(120f, 120f, 120f) : V(35f, 35f, 87.5f);
                bool actual = collision.OverlapsActorColliders(actor, location, extent);
                if (actual) hits++;
                if (collision.ColliderAt(index).Location != actor.Location) deferred++;
                collision.SyncCollider(collision.ColliderAt(index));
                Assert.Equal(collision.OverlapsActorColliders(actor, location, extent), actual);
            }
        }

        Assert.True(hits > 100, $"Expected contact coverage, got {hits} hits.");
        Assert.True(deferred > 100, $"Expected distant mover refits to be deferred, got {deferred}.");
        output.WriteLine($"480 actor overlap queries match eager refits; {hits} contacts and {deferred} deferred far transforms.");
    }

    [Fact]
    public void CylinderFilteringUsesLiveShapeFlagsOwnersAndStableTieOrder()
    {
        var world = new World(4);
        CollisionWorld collision = CollisionWorld.Build(new CollisionDataBuilder().Build());
        collision.BindLevel(world.Level);
        TestPawn first = world.Level.SpawnActor(new TestPawn(), V(4000f, 0f, 0f), default)!;
        TestPawn second = world.Level.SpawnActor(new TestPawn(), V(4000f, 0f, 0f), default)!;
        Actor source = world.Level.SpawnActor(new CandidateActor(), default, default)!;
        ETraceFlags flags = ETraceFlags.TRACE_Pawns | ETraceFlags.TRACE_Blocking;
        FVector start = V(-200f, 0f, 0f), end = V(200f, 0f, 0f);
        Assert.False(collision.LineCheck(out _, source, end, start, default, flags));
        source.bBlockActors = source.bBlockPlayers = true;
        first.Location = second.Location = default;
        Assert.True(collision.LineCheck(out FCheckResult hit, source, end, start, default, flags));
        Assert.Same(first, hit.Actor);
        first.CollisionRadius = 1f;
        Assert.True(collision.LineCheck(out hit, source, end, start, default, flags));
        Assert.Same(second, hit.Actor);
        source.SetOwner(second);
        Assert.True(collision.LineCheck(out hit, source, end, start, default, flags));
        Assert.Same(first, hit.Actor);
        first.bCollideActors = false;
        Assert.False(collision.LineCheck(out _, source, end, start, default, flags));
        first.bCollideActors = true;
        first.CollisionHeight = 5f;
        Assert.False(collision.PointCheck(out _, source, V(0f, 0f, 30f), V(1f, 1f, 1f), flags));
        first.CollisionHeight = 40f;
        Assert.True(collision.PointCheck(out hit, source, V(0f, 0f, 30f), V(1f, 1f, 1f), flags));
        Assert.Same(first, hit.Actor);
    }

    [Fact]
    public void DistantMovingMeshDiagnosticReportsLazyVersusEagerTransformCost()
    {
        var world = new World(19);
        var builder = new CollisionDataBuilder();
        var actors = new Dictionary<string, Actor>();
        var boxes = new List<(FVector, FVector)>();
        for (int i = 0; i < 100; i++) boxes.Add((V(i * 5f, -20f, -20f), V(i * 5f + 3f, 20f, 20f)));
        for (int i = 0; i < 32; i++)
        {
            string id = "mover" + i;
            Actor actor = world.Level.SpawnActor(new CandidateActor(), V(10000f + i * 1000f, 0f, 0f), default)!;
            actors.Add(id, actor);
            builder.AddBoxMeshCollider(ColliderKind.Mover, id, CollisionDataBuilder.WorldFlags, boxes, actor.Location);
        }

        CollisionWorld collision = CollisionWorld.Build(builder.Build());
        collision.BindLevel(world.Level, id => actors.GetValueOrDefault(id));
        double Measure(bool eager)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < 100; i++)
            {
                foreach (Actor actor in actors.Values) actor.Rotation = new FRotator(0, (i + 1) * 200, 0);
                if (eager) EagerSync(collision);
                Assert.False(collision.LineCheck(out _, null, V(100f, 0f, 0f), V(-100f, 0f, 0f), default, ETraceFlags.TRACE_AllColliding));
            }

            return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Measure(false);
        Measure(true);
        double lazyMs = Measure(false), eagerMs = Measure(true);
        output.WriteLine($"100 poses / 32 distant moving meshes / 1,200 triangles each: lazy bounds {lazyMs:F3} ms, eager refit {eagerMs:F3} ms. Synthetic transform cost, not game FPS.");
    }
}
