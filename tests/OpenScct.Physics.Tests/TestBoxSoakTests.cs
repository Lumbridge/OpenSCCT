using System;
using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Content.Loading;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Physics.Tests.Geo;

namespace OpenScct.Physics.Tests;

/// <summary>TestBox loaded through the content reader and map loader, with pawns walking at random.</summary>
public sealed class TestBoxSoakTests
{
    private readonly ITestOutputHelper _out;

    public TestBoxSoakTests(ITestOutputHelper output) => _out = output;

    private sealed class Run
    {
        public required World World { get; init; }
        public required PhysicsWorld Physics { get; init; }
        public required List<TestPawn> Pawns { get; init; }
    }

    private static Run Load(ulong seed)
    {
        SvMap map = SvMap.Load(TestPaths.TestBox);
        var world = new World(seed);
        MapLoadResult loaded = new MapLoader().Load(world, map);
        PhysicsWorld physics = PhysicsWorld.Attach(map, loaded);
        Assert.Same(physics.Driver, world.Level.PhysicsDriver);

        var pawns = new List<TestPawn>();
        foreach (Actor? a in loaded.Actors)
        {
            if (a is { } start && loaded.RecordOf(start)?.Class == "Engine.PlayerStart")
            {
                TestPawn p = world.Level.SpawnActor(new TestPawn(), start.Location, start.Rotation)!;
                p.Physics = EPhysics.PHYS_Falling;
                var c = world.Level.SpawnActor(new TestController(), start.Location, default)!;
                c.Pawn = p;
                p.Controller = c;
                pawns.Add(p);
            }
        }

        physics.Collision.NotifyCollisionChanged();
        return new Run { World = world, Physics = physics, Pawns = pawns };
    }

    [Fact]
    public void PawnsSpawnAndSettleOnTheFloor()
    {
        Run run = Load(1);
        Assert.Equal(4, run.Pawns.Count);
        run.World.Step(60);
        foreach (TestPawn p in run.Pawns)
        {
            Assert.Equal(EPhysics.PHYS_Walking, p.Physics);
            Assert.Equal(WalkZ(), p.Location.Z, 2);
            Assert.Same(run.World.Level.Info, p.Base);
        }

        // TestBox: Room A, Room B (spawns), and the zone actors resolved by id.
        Assert.Equal("Room A", run.Physics.Collision.GetZoneInfo(run.Pawns[0].Location)?.LocationName);
        Assert.Equal("Room B", run.Physics.Collision.GetZoneInfo(run.Pawns[2].Location)?.LocationName);
    }

    [Fact]
    public void TheClosedDoorBlocksTheCorridorAndTheVolumeBlocksBoxes()
    {
        Run run = Load(1);
        CollisionWorld w = run.Physics.Collision;
        var pawn = V(35f, 35f, 87.5f);
        // Down corridor AB from room A: the closed sliding door (Mover0 at x 1280) stops a pawn.
        Assert.True(w.LineCheck(out FCheckResult door, run.Pawns[0], V(1500f, 512f, 88f), V(900f, 512f, 88f), pawn, ETraceFlags.TRACE_AllBlocking));
        Assert.Equal("Mover0", w.GetCollider(door.Collider).ActorId);
        // The door is a mesh: a 600 uu sweep stops 1 uu short of it.
        Assert.Equal(1272f - 35f - 1f, door.Location.X, 2);
    }

    /// <summary>Random walking for <paramref name="ticks"/>; returns a digest of every pawn's final state.</summary>
    private static string Soak(ulong seed, int ticks, Action<Run, int>? check = null)
    {
        Run run = Load(seed);
        var r = new Random((int)seed);
        for (int t = 0; t < ticks; t++)
        {
            if (t % 30 == 0)
            {
                foreach (TestPawn p in run.Pawns)
                {
                    double a = r.NextDouble() * Math.PI * 2.0;
                    p.Acceleration = r.Next(5) == 0 ? default : V((float)(Math.Cos(a) * 3000.0), (float)(Math.Sin(a) * 3000.0), 0f);
                    p.WalkSpeed = 150f + r.Next(300);
                    if (r.Next(6) == 0 && p.Physics == EPhysics.PHYS_Walking)
                    {
                        run.Physics.Driver.MyDoJump(p);
                    }
                }
            }

            run.World.Step();
            check?.Invoke(run, t);
        }

        var parts = new List<string>();
        foreach (TestPawn p in run.Pawns)
        {
            parts.Add($"{p.Location}{p.Velocity}{p.Physics}");
        }

        return string.Join("|", parts);
    }

    [Fact]
    public void RandomWalkNeverFallsThroughOrSinksIntoGeometry()
    {
        CollisionWorld? world = null;
        int checks = 0;
        int walking = 0;
        string digest = Soak(7, 60 * 60, (run, t) =>
        {
            world = run.Physics.Collision;
            foreach (TestPawn p in run.Pawns)
            {
                FVector l = p.Location;
                Assert.True(l.Z >= StandZ() - 0.01f, $"tick {t}: {p} fell through to {l}");
                Assert.True(l.Z < 512f, $"tick {t}: {p} above the ceiling at {l}");
                Assert.True(l.X > world.WorldMin.X && l.X < world.WorldMax.X && l.Y > world.WorldMin.Y && l.Y < world.WorldMax.Y,
                    $"tick {t}: {p} left the map at {l}");
                bool inside = world.PointCheck(out FCheckResult h, p, l, V(34.95f, 34.95f, 87.45f), ETraceFlags.TRACE_World);
                Assert.False(inside, $"tick {t}: {p} inside collider {h.Collider} at {l}");
                checks++;
                if (p.Physics == EPhysics.PHYS_Walking)
                {
                    walking++;
                }
            }
        });
        _out.WriteLine($"{checks} pawn-ticks checked, {walking} walking; final {digest}");
        Assert.True(walking > checks / 2);
    }

    [Fact]
    public void TheSameInputsGiveBitIdenticalResults()
    {
        Assert.Equal(Soak(11, 900), Soak(11, 900));
    }

    [Fact]
    public void SteadyStateTicksDoNotAllocate()
    {
        // A repeating pattern: pawns pace into walls and each other, through a trigger, and onto a lift that goes
        // up and down. After one period every list has grown to size, so later periods must not allocate.
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-600f, 300f, 0f), V(600f, 400f, 300f));
        b.AddBoxHullCollider(ColliderKind.Mover, "Lift", CollisionDataBuilder.WorldFlags, V(-100f, -100f, -20f), V(100f, 100f, 0f),
            V(-400f, -300f, 20f));
        TestMover? lift = null;
        Scene s = Scene.Create(b.Build(), level =>
        {
            lift = level.SpawnActor(new TestMover(), V(-400f, -300f, 20f), default)!;
            lift.BasePos = lift.Location;
            lift.KeyPos[1] = V(0f, 0f, 150f);
            return new Dictionary<string, Actor> { ["Lift"] = lift };
        });
        s.Level.SpawnActor(new TestTrigger(), V(0f, 0f, 64f), default);
        var pawns = new List<TestPawn>();
        for (int i = 0; i < 6; i++)
        {
            pawns.Add(s.SpawnPawn(V(-300f + 120f * i, 0f, StandZ() + 1f)));
        }

        TestPawn rider = s.SpawnPawn(V(-400f, -300f, StandZ(20f) + 1f));
        void Drive(int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                int phase = (int)(s.Level.TickCount % 240);
                if (phase == 0)
                {
                    lift!.InterpolateTo(1, 1f);
                }
                else if (phase == 120)
                {
                    lift!.InterpolateTo(0, 1f);
                }

                if (phase % 60 == 0)
                {
                    float dir = phase % 120 == 0 ? 1f : -1f;
                    for (int i = 0; i < pawns.Count; i++)
                    {
                        pawns[i].Acceleration = V(i % 2 == 0 ? 3000f * dir : 0f, 3000f * dir, 0f);
                    }
                }

                s.Step(1);
            }
        }

        Drive(480);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Drive(960);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Same(lift, rider.Base);
    }
}
