using System.Collections.Generic;
using System.IO;
using OpenScct.Content;
using OpenScct.Content.Collision;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

/// <summary>
/// The navigation graph built from collision and GEs, and the follower that plays it with a player's keys, on
/// synthetic levels shaped like the traversal tests' (spec/movement/ledge.md, pipe-vertical.md, zip-line.md).
/// </summary>
public sealed class NavGraphTests
{
    private readonly ITestOutputHelper _output;

    public NavGraphTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static NavGraph Build(CollisionDataBuilder level, NavProfile profile, TraversalDoc? ges, FVector min, FVector max) =>
        NavBuilder.Build(CollisionWorld.Build(level.Build()), min, max, profile, ges?.Ges);

    /// <summary>A floor, a block 250 high (y 100 to 600) with an LG on its front lip, as in the traversal tests.</summary>
    private static (CollisionDataBuilder Level, TraversalDoc Ges) LedgeLevel()
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-300f, 100f, 0f), V(300f, 600f, 250f));
        return (b, Doc(Ge(0, GEKind.LG, V(-300f, 100f, 250f), V(300f, 100f, 250f))));
    }

    [Fact]
    public void FloorsBecomeNodesWithWalkEdgesBothWays()
    {
        NavGraph g = Build(Floor(), NavProfile.Spy, null, V(-160f, -160f, -100f), V(160f, 160f, 300f));
        Assert.Equal(11 * 11, g.NodeCount);
        Assert.True(g.CountEdges(NavEdgeKind.Walk) > 4 * 11 * 10, $"walk edges {g.CountEdges(NavEdgeKind.Walk)}");
        var path = new List<NavStep>();
        Assert.True(g.FindPath(g.NearestNode(V(-160f, -160f, 1f)), g.NearestNode(V(160f, 160f, 1f)), path));
        Assert.All(path, s => Assert.Equal(NavEdgeKind.Walk, s.Kind));
    }

    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void RotatedMeshPassageUsesMovementClearance(PawnTeam team)
    {
        CollisionDataBuilder level = Floor();
        level.AddBoxMeshCollider(ColliderKind.MeshPoly, null, CollisionDataBuilder.WorldFlags,
            [(V(-200f, -500f, 0f), V(-45f, 500f, 300f)),
             (V(45f, -500f, 0f), V(200f, 500f, 300f))], rotation: new FRotator(0, 8192, 0));
        ICollisionWorld collision = CollisionWorld.Build(level.Build());
        var body = V(35f, 35f, 87.5f);
        var center = V(0f, 0f, 90f);
        const ETraceFlags flags = ETraceFlags.TRACE_Blocking | ETraceFlags.TRACE_Level | ETraceFlags.TRACE_LevelGeometry;
        Assert.True(collision.PointCheck(out _, null, center, body, flags));
        Assert.False(collision.OverlapMoveShape(out _, null, center, body, flags));
        NavGraph graph = NavBuilder.Build(collision, V(-320f, -320f, -100f), V(320f, 320f, 400f),
            team == PawnTeam.Spy ? NavProfile.Spy : NavProfile.Merc);
        int node = graph.NearestNode(center, 90f);
        Assert.True(node >= 0);
        Assert.InRange(NavFollower.Horizontal(graph.Node(node).Floor), 0f, 1f);
        Assert.True(graph.Node(node).Standing);
        var path = new List<NavStep>();
        Assert.True(graph.FindPath(graph.NearestNode(V(128f, -128f, 1f)), graph.NearestNode(V(-128f, 128f, 1f)), path));
    }

    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void TheFollowerWalksDownCloselySpacedSpawnStairs(PawnTeam team)
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-200f, -300f, 0f), V(200f, 0f, 240f));
        for (int step = 0; step < 10; step++)
        {
            level.AddBox(V(-200f, step * 24f, 0f), V(200f, (step + 1) * 24f, 240f - step * 24f));
        }
        PawnRig rig = Rig(level, null, V(0f, -160f, 240f), team);
        AiWorld world = World(rig, V(-128f, -256f, -100f), V(128f, 512f, 500f));
        var follower = new NavFollower(world, team == PawnTeam.Spy);
        var route = new List<NavStep>();
        Assert.True(follower.Graph.FindPath(follower.Graph.NearestNode(rig.Pawn.Location, rig.Pawn.CollisionHeight),
            follower.Graph.NearestNode(V(0f, 400f, 1f)), route));
        var trace = new List<string>();
        Assert.True(Follow(rig, follower, V(0f, 400f, 0f), 12f, trace), string.Join("\n", trace));
    }

    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void UnreachableObjectiveAdvancesToReachableFrontierWithoutCrossingTheWall(PawnTeam team)
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-400f, -16f, 0f), V(400f, 16f, 300f));
        PawnRig rig = Rig(level, null, V(0f, -256f, 0f), team);
        AiWorld world = World(rig, V(-256f, -320f, -100f), V(256f, 320f, 400f));
        var follower = new NavFollower(world, team == PawnTeam.Spy);
        NavGraph graph = follower.Graph;
        int start = graph.NearestNode(rig.Pawn.Location, rig.Pawn.CollisionHeight);
        int end = graph.NearestNode(V(0f, 256f, 1f));
        var path = new List<NavStep>();
        Assert.False(graph.FindPath(start, end, path));
        Assert.True(graph.FindPath(start, end, path, allowPartial: true));
        Assert.NotEmpty(path);
        Assert.NotEqual(end, path[^1].Node);
        Assert.False(Follow(rig, follower, V(0f, 256f, 0f), 8f));
        Assert.True(follower.Failed);
        Assert.False(follower.Arrived);
        Assert.InRange(rig.Pawn.Location.Y, -100f, -50f);
    }

    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void SpawnExitIgnoresMeshesThatDoNotBlockPlayers(PawnTeam team)
    {
        CollisionDataBuilder level = Floor();
        level.AddBoxMeshCollider(ColliderKind.MeshPoly, null,
            CollisionDataBuilder.WorldFlags & ~ColliderFlags.BlockPlayers,
            [(V(-400f, -16f, 0f), V(400f, 16f, 300f))]);
        PawnRig rig = Rig(level, null, V(0f, -160f, 0f), team);
        AiWorld world = World(rig, V(-256f, -256f, -100f), V(256f, 256f, 400f));
        var follower = new NavFollower(world, team == PawnTeam.Spy);
        Assert.True(Follow(rig, follower, V(0f, 160f, 0f), 8f));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FenceCrossingRecoversWhenItsRouteIsLost(bool farSide)
    {
        CollisionDataBuilder level = Floor();
        ushort material = level.AddMaterial(0, CollisionMaterialFlags.Climbable);
        level.AddBox(V(-500f, 100f, 0f), V(500f, 102f, 400f), material);
        TraversalDoc ges = Doc(Ge(0, GEKind.FENCE, V(-500f, 100f, 400f), V(500f, 100f, 400f)));
        PawnRig rig = Rig(level, ges, V(0f, -100f, 0f));
        for (int tick = 0; tick < 600; tick++)
        {
            rig.Step(new PlayerInputFrame { BT_Up = 1, FastSpeed = true });
            if (rig.Pawn.Physics == EPhysics.PHYS_OnVerticalPlane && rig.Pawn.Location.Z > 250f &&
                (rig.Pawn.Location.Y > 102f) == farSide) break;
        }
        Assert.Equal(EPhysics.PHYS_OnVerticalPlane, rig.Pawn.Physics);
        Assert.Equal(farSide, rig.Pawn.Location.Y > 102f);
        AiWorld world = World(rig, V(-600f, -300f, -100f), V(600f, 500f, 600f), ges);
        var follower = new NavFollower(world, spy: true);
        var trace = new List<string>();
        Assert.True(Follow(rig, follower, V(0f, 350f, 0f), 15f, trace), string.Join("\n", trace));
        Assert.True(rig.Pawn.Location.Y > 300f);
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
    }

    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void TheFollowerCrossesASmallStepWhoseRiserSeparatesSampledNodes(PawnTeam team)
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-400f, -400f, 0f), V(0f, 400f, 32f));
        PawnRig rig = Rig(level, null, V(-160f, 0f, 32f), team, yaw: 0);
        AiWorld world = World(rig, V(-256f, -128f, -100f), V(256f, 128f, 300f));
        var follower = new NavFollower(world, team == PawnTeam.Spy);
        Assert.True(Follow(rig, follower, V(160f, 0f, 0f), 8f), "the walker could not step down across the sampled-node gap");
        Assert.True(Follow(rig, follower, V(-160f, 0f, 32f), 8f), "the walker could not step back up");
    }

    [Fact]
    public void SmallStepConnectionsCannotBridgeAnUnsupportedGap()
    {
        var level = new CollisionDataBuilder();
        level.AddBox(V(-400f, -400f, -64f), V(-16f, 400f, 16f));
        level.AddBox(V(16f, -400f, -64f), V(400f, 400f, 0f));
        NavGraph graph = Build(level, NavProfile.Spy, null, V(-256f, -128f, -100f), V(256f, 128f, 300f));
        var path = new List<NavStep>();
        Assert.False(graph.FindPath(graph.NearestNode(V(-128f, 0f, 17f)), graph.NearestNode(V(128f, 0f, 1f)), path));
    }

    [Fact]
    public void ACrawlGapGetsCrouchEdgesThatOnlyFitASpy()
    {
        // A wall across x = 0 with a gap 120 wide and 120 high: a crouched spy (2 x 50) fits, a crouched merc (2 x 68)
        // does not, and nobody fits standing.
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-20f, -2000f, 120f), V(20f, 2000f, 400f));
        b.AddBox(V(-20f, -2000f, 0f), V(20f, -60f, 120f));
        b.AddBox(V(-20f, 60f, 0f), V(20f, 2000f, 120f));
        FVector min = V(-200f, -200f, -100f);
        FVector max = V(200f, 200f, 300f);
        NavGraph spy = Build(b, NavProfile.Spy, null, min, max);
        NavGraph merc = Build(b, NavProfile.Merc, null, min, max);
        var path = new List<NavStep>();
        Assert.True(spy.FindPath(spy.NearestNode(V(-180f, 0f, 1f)), spy.NearestNode(V(180f, 0f, 1f)), path));
        Assert.Contains(path, s => s.Kind == NavEdgeKind.Crouch);
        Assert.False(merc.FindPath(merc.NearestNode(V(-180f, 0f, 1f)), merc.NearestNode(V(180f, 0f, 1f)), path));
    }

    [Fact]
    public void ALedgeGetsAClimbLinkForSpiesAndNoneForMercs()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = LedgeLevel();
        FVector min = V(-400f, -300f, -100f);
        FVector max = V(400f, 700f, 400f);
        NavGraph spy = Build(level, NavProfile.Spy, ges, min, max);
        NavGraph merc = Build(level, NavProfile.Merc, ges, min, max);
        Assert.True(spy.CountLinks(NavEdgeKind.LedgeClimb) >= 3, $"links {spy.CountLinks(NavEdgeKind.LedgeClimb)}");
        Assert.Equal(0, merc.CountLinks(NavEdgeKind.LedgeClimb));
        NavLink link = spy.Links[0];
        Assert.InRange(link.Height, 248f, 252f);
        Assert.Equal(16384, link.Yaw);
        Assert.InRange(link.Start.Y, 50f, 60f);

        // Up only by the ledge; down by a drop (250 is under the spy's 600 and the merc's 400).
        var path = new List<NavStep>();
        Assert.True(spy.FindPath(spy.NearestNode(V(0f, -200f, 1f)), spy.NearestNode(V(0f, 400f, 251f)), path));
        Assert.Contains(path, s => s.Kind == NavEdgeKind.LedgeClimb);
        Assert.False(merc.FindPath(merc.NearestNode(V(0f, -200f, 1f)), merc.NearestNode(V(0f, 400f, 251f)), path));
        Assert.True(merc.FindPath(merc.NearestNode(V(0f, 400f, 251f)), merc.NearestNode(V(0f, -200f, 1f)), path));
        Assert.Contains(path, s => s.Kind == NavEdgeKind.Drop);
    }

    [Fact]
    public void DropsDeeperThanTheFallDamageHeightAreLeftOut()
    {
        CollisionDataBuilder b = Floor();
        // The spy plans drops to 700 uu (a few points of fall damage), not 750.
        b.AddBox(V(-300f, 100f, 0f), V(300f, 600f, 750f));
        NavGraph spy = Build(b, NavProfile.Spy, null, V(-400f, -300f, -100f), V(400f, 700f, 900f));
        var path = new List<NavStep>();
        Assert.False(spy.FindPath(spy.NearestNode(V(0f, 400f, 751f)), spy.NearestNode(V(0f, -200f, 1f)), path));
    }

    /// <summary>
    /// A drop needs room to land: a shelf 80 uu deep (one row of nodes) in front of a higher platform, over a deeper floor, would catch a
    /// walker by the arms or let it fall on past, so no drop ends on it; the drops past it to the floor stay.
    /// </summary>
    [Fact]
    public void DropsOntoANarrowShelfAreLeftOut()
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-300f, 100f, 0f), V(300f, 600f, 500f));
        b.AddBox(V(-300f, 20f, 0f), V(300f, 100f, 250f));
        NavGraph spy = Build(b, NavProfile.Spy, null, V(-400f, -300f, -100f), V(400f, 700f, 700f));
        int shelfNodes = 0;
        int dropsToShelf = 0;
        int dropsToFloor = 0;
        for (int n = 0; n < spy.NodeCount; n++)
        {
            if (spy.Node(n).Floor.Z < 400f)
            {
                continue;
            }

            foreach ((int to, NavEdgeKind kind, _, _) in spy.EdgesOf(n))
            {
                if (kind != NavEdgeKind.Drop)
                {
                    continue;
                }

                float z = spy.Node(to).Floor.Z;
                if (z is > 200f and < 300f)
                {
                    dropsToShelf++;
                }
                else if (z < 50f)
                {
                    dropsToFloor++;
                }
            }
        }

        for (int n = 0; n < spy.NodeCount; n++)
        {
            if (spy.Node(n).Floor.Z is > 200f and < 300f)
            {
                shelfNodes++;
            }
        }

        Assert.True(shelfNodes > 0, "the shelf has no nodes");
        Assert.Equal(0, dropsToShelf);
        Assert.True(dropsToFloor > 0, "no drop from the platform to the floor");
    }

    [Fact]
    public void BuildsAreDeterministicAndTheCacheFileRoundTrips()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = LedgeLevel();
        FVector min = V(-400f, -300f, -100f);
        FVector max = V(400f, 700f, 400f);
        NavGraph a = Build(level, NavProfile.Spy, ges, min, max);
        NavGraph b = Build(level, NavProfile.Spy, ges, min, max);
        Assert.Equal(a.ContentDigest(), b.ContentDigest());
        using var ms = new MemoryStream();
        a.Write(ms, "test-map");
        ms.Position = 0;
        NavGraph? read = NavGraph.Read(ms, "test-map", NavProfile.Spy);
        Assert.NotNull(read);
        Assert.Equal(a.ContentDigest(), read.ContentDigest());
        ms.Position = 0;
        Assert.Null(NavGraph.Read(ms, "other-map", NavProfile.Spy));
        ms.Position = 0;
        Assert.Null(NavGraph.Read(ms, "test-map", NavProfile.Merc));
    }

    [Fact]
    public void StealthRoutesGoRoundTheLight()
    {
        // Two lanes round a wall: the north one lit, the south one dark. Plain plans take either; stealth takes the dark.
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-150f, -150f, 0f), V(150f, 150f, 300f));
        FVector min = V(-400f, -400f, -100f);
        FVector max = V(400f, 400f, 400f);
        NavGraph g = Build(b, NavProfile.Spy, null, min, max);
        var light = new AiLightField([new AiLight(V(0f, 300f, 150f), default, 255f, 0, 255, 12f, 128, 0, -1, false)]);
        g.ApplyLight(light);
        var path = new List<NavStep>();
        Assert.True(g.FindPath(g.NearestNode(V(-350f, 0f, 1f)), g.NearestNode(V(350f, 0f, 1f)), path, stealth: 1f));
        Assert.All(path, s => Assert.True(s.Point.Y < 100f, $"lit step at {s.Point}"));
    }

    // ---- following ---------------------------------------------------------------------------------------------

    [Fact]
    public void TheFollowerWalksRoundAWall()
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-300f, 200f, 0f), V(300f, 240f, 300f));
        PawnRig rig = Rig(b, null, V(0f, 0f, 0f));
        AiWorld world = World(rig, V(-500f, -200f, -100f), V(500f, 600f, 400f));
        var follower = new NavFollower(world, spy: true);
        var trace = new List<string>();
        bool ok = Follow(rig, follower, V(0f, 450f, 0f), 15f, trace);
        _output.WriteLine(string.Join("\n", trace));
        Assert.True(ok);
        Assert.InRange(rig.Pawn.Location.Y, 420f, 480f);
    }

    /// <summary>
    /// Playtest (play-team, the bot offering a boost): the bot walked into the player crouched at the foot of a wall,
    /// pushed for 2 s, and the stuck wiggle's jump landed it on the player's head, a drop attack that knocked him down.
    /// A walker steps around a pawn in its way and never jumps next to one.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheFollowerStepsRoundAPawnInItsWayWithoutJumpingOnHim(bool crouchedMate)
    {
        CollisionDataBuilder b = Floor();
        PawnRig rig = Rig(b, null, V(0f, 0f, 0f));
        RigMate mate = rig.AddMate(PawnTeam.Spy, V(4f, 260f, 0f), 49152, crouchedMate);
        rig.WaitStill(60);
        AiWorld world = World(rig, V(-500f, -200f, -100f), V(500f, 800f, 400f));
        var follower = new NavFollower(world, spy: true);
        var trace = new List<string>();
        bool jumped = false;
        bool ok = false;
        FVector goal = V(0f, 560f, 0f);
        Assert.True(follower.SetGoal(rig.Pawn, goal));
        for (int i = 0; i < 12 * Sim.Hz && !ok; i++)
        {
            NavCommand c = follower.Steer(rig.Pawn, rig.Level.TimeSeconds, rig.Controller.Rotation.Yaw & 0xFFFF, sneak: false);
            ok = follower.Arrived;
            jumped |= c.Keys.Command1 == AiCommand.Jump || c.Keys.Command2 == AiCommand.Jump;
            rig.Step(new PlayerInputFrame
            {
                BT_Up = c.Keys.BT_Up,
                BT_Left = c.Keys.BT_Left,
                BT_Right = c.Keys.BT_Right,
                Jump = c.Keys.Command1 == AiCommand.Jump || c.Keys.Command2 == AiCommand.Jump,
                View = c.Yaw is { } yaw ? new FRotator(0, yaw, 0) : null,
                FastSpeed = true,
            });
            if (i % 30 == 0 || (i > 40 && i < 100 && i % 3 == 0))
            {
                trace.Add($"{rig.Level.TimeSeconds:F2} {rig.Controller.GetStateName()} {rig.Pawn.Physics} at {rig.Pawn.Location} keys up {c.Keys.BT_Up} left {c.Keys.BT_Left} right {c.Keys.BT_Right} yaw {c.Yaw} view {rig.Controller.Rotation.Yaw & 0xFFFF} next {follower.NextIndex}/{follower.Path.Count}");
            }
        }

        _output.WriteLine(string.Join("\n", trace));
        Assert.False(jumped, "the walker jumped next to the pawn in its way\n" + string.Join("\n", trace));
        Assert.True(ok, "the walker did not get past the pawn\n" + string.Join("\n", trace));
        Assert.False(mate.Pawn.bLyingOnGround);
        Assert.Equal(OpenScct.Core.Engine.EPhysics.PHYS_Walking, mate.Pawn.Physics);
    }

    /// <summary>
    /// Playtest (MounE, the player hanging on the spawn ledge, then standing at its foot): the spy bot on his team stood
    /// at the edge for 59 s, creeping 0.1 uu/s, because its wait for a teammate under the landing started again on the
    /// tick after it ran out. A walker waits a while, then goes down another way.
    /// </summary>
    [Fact]
    public void TheFollowerGoesDownAnotherWayWhenATeammateStaysUnderTheLanding()
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-200f, 100f, 0f), V(200f, 600f, 200f));
        PawnRig rig = Rig(b, null, V(0f, 300f, 200f), yaw: 49152);
        RigMate mate = rig.AddMate(PawnTeam.Spy, V(0f, 40f, 0f), 16384);
        rig.WaitStill(60);
        AiWorld world = World(rig, V(-500f, -400f, -100f), V(500f, 800f, 400f));
        var follower = new NavFollower(world, spy: true);
        var trace = new List<string>();
        bool ok = Follow(rig, follower, V(0f, -250f, 0f), 15f, trace);
        _output.WriteLine(string.Join("\n", trace));
        Assert.True(ok, string.Join("\n", trace));
        Assert.True(rig.Pawn.Location.Z < 150f, $"z {rig.Pawn.Location.Z}");
        Assert.False(mate.Pawn.bLyingOnGround, "the walker dropped on the mate");
    }

    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void TheFollowerRecoversALadderGrabAfterItsRouteWasCleared(PawnTeam team)
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-300f, 105f, 0f), V(300f, 900f, 400f));
        TraversalDoc ges = Doc(Ge(0, GEKind.LADDER, V(0f, 100f, 0f), V(0f, 100f, 400f), V(0f, 90f, 0f)));
        PawnRig rig = Rig(level, ges, V(0f, 20f, 0f), team);
        for (int i = 0; i < 200 && rig.Pawn.Physics != EPhysics.PHYS_OnLadder; i++)
        {
            rig.Step(new PlayerInputFrame { BT_Up = 1 });
        }

        Assert.Equal(EPhysics.PHYS_OnLadder, rig.Pawn.Physics);
        for (int i = 0; i < 90; i++)
        {
            rig.Step(new PlayerInputFrame { BT_Up = 1 });
        }
        AiWorld world = World(rig, V(-400f, -300f, -100f), V(400f, 1000f, 600f), ges);
        var follower = new NavFollower(world, team == PawnTeam.Spy);
        FVector goal = V(0f, -150f, 0f);
        Assert.True(follower.SetGoal(rig.Pawn, goal));
        NavCommand command = follower.Steer(rig.Pawn, rig.Level.TimeSeconds, rig.Controller.Rotation.Yaw, sneak: false);
        Assert.Equal(1, command.Keys.BT_Down);
        Assert.Equal(0, command.Keys.BT_Up);
        Assert.True(Follow(rig, follower, goal, 12f));
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
    }

    [Fact]
    public void TheFollowerClimbsALedge()
    {
        (CollisionDataBuilder level, TraversalDoc ges) = LedgeLevel();
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, level.Build(), ges);
        rig.Place(V(0f, -200f, 89.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), crouched: false);
        rig.SetFastSpeed(true);
        rig.WaitStill(120);
        AiWorld world = World(rig, V(-400f, -300f, -100f), V(400f, 700f, 400f), ges);
        var follower = new NavFollower(world, spy: true);
        var trace = new List<string>();
        bool ok = Follow(rig, follower, V(0f, 350f, 250f), 20f, trace);
        _output.WriteLine(string.Join("\n", trace));
        Assert.True(ok, string.Join("\n", trace));
        Assert.True(rig.Pawn.Location.Z > 250f, $"z {rig.Pawn.Location.Z}");
        Assert.Equal(1, follower.LinksDone);
    }

    [Fact]
    public void TheFollowerRidesAZipLineOffABalcony()
    {
        // A balcony 1000 up (too high to drop) round (0, 0); the line from 300 above it down to (0, 3000, 300).
        CollisionDataBuilder b = Floor(4000f);
        b.AddBox(V(-200f, -300f, 0f), V(200f, 200f, 1000f));
        TraversalDoc ges = Doc(Ge(0, GEKind.ZL, V(0f, 0f, 1300f), V(0f, 3000f, 300f)));
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, b.Build(), ges);
        rig.Place(V(0f, -150f, 1089.5f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), crouched: false);
        rig.SetFastSpeed(true);
        rig.WaitStill(120);
        AiWorld world = World(rig, V(-300f, -400f, -100f), V(300f, 3200f, 1400f), ges);
        Assert.Equal(1, world.SpyNav.CountLinks(NavEdgeKind.ZipLine));
        var follower = new NavFollower(world, spy: true);
        var trace = new List<string>();
        bool ok = Follow(rig, follower, V(0f, 2900f, 0f), 30f, trace);
        _output.WriteLine(string.Join("\n", trace));
        Assert.True(ok, string.Join("\n", trace));
        Assert.True(follower.LinksDone >= 1);
    }

    [Fact]
    public void APipeGetsAClimbLink()
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-300f, 230f, 0f), V(300f, 900f, 600f));
        TraversalDoc ges = Doc(Ge(0, GEKind.PIPE, V(0f, 200f, 0f), V(0f, 200f, 600f), V(0f, 190f, 0f)));
        NavGraph spy = Build(b, NavProfile.Spy, ges, V(-400f, -200f, -100f), V(400f, 900f, 800f));
        Assert.Equal(1, spy.CountLinks(NavEdgeKind.PipeClimb));
        NavLink link = spy.Links[0];
        Assert.Equal(16384, link.Yaw);
        Assert.True(link.End.Z > 590f);
    }
}
