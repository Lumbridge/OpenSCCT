using System.Collections.Generic;
using System.IO;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class NavTraversalImprovementsTests
{
    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void DroppingOffALadderDoesNotCountAsReachingItsTop(PawnTeam team)
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-300f, 105f, 0f), V(300f, 900f, 400f));
        TraversalDoc ges = Doc(Ge(0, GEKind.LADDER, V(0f, 100f, 0f), V(0f, 100f, 400f), V(0f, 90f, 0f)));
        PawnRig rig = Rig(level, ges, V(0f, 65f, 0f), team);
        AiWorld world = World(rig, V(-400f, -300f, -100f), V(400f, 1000f, 600f), ges);
        var follower = new NavFollower(world, team == PawnTeam.Spy);
        Assert.True(follower.SetGoal(rig.Pawn, V(0f, 500f, 400f)));
        for (int tick = 0; tick < 600 && NavFollower.Feet(rig.Pawn).Z < 180f; tick++)
        {
            NavCommand c = follower.Steer(rig.Pawn, rig.Level.TimeSeconds, rig.Controller.Rotation.Yaw, false);
            rig.Step(new PlayerInputFrame { BT_Up = c.Keys.BT_Up, BT_Down = c.Keys.BT_Down,
                FastSpeed = true, View = c.Yaw is { } yaw ? new FRotator(0, yaw, 0) : null });
        }
        Assert.Equal(EPhysics.PHYS_OnLadder, rig.Pawn.Physics);
        Assert.True(follower.ActiveLink >= 0);
        rig.Step(new PlayerInputFrame { bCrouch = 1 });
        for (int tick = 0; tick < 240; tick++) rig.Step(default);
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.InRange(NavFollower.Feet(rig.Pawn).Z, 0f, 4f);
        follower.Steer(rig.Pawn, rig.Level.TimeSeconds, rig.Controller.Rotation.Yaw, false);
        Assert.Equal(0, follower.LinksDone);
        Assert.Equal(1, follower.LinksFailed);
    }

    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void FollowerJumpsAcrossAnUnsupportedGap(PawnTeam team)
    {
        var level = new CollisionDataBuilder();
        level.AddBox(V(-300f, -700f, -64f), V(300f, 0f, 0f));
        level.AddBox(V(-300f, 110f, -64f), V(300f, 700f, 0f));
        PawnRig rig = Rig(level, null, V(0f, -250f, 0f), team);
        AiWorld world = World(rig, V(-200f, -500f, -100f), V(200f, 600f, 400f));
        var follower = new NavFollower(world, team == PawnTeam.Spy);
        Assert.True(follower.Graph.CountLinks(NavEdgeKind.Jump) > 0);
        var trace = new List<string>();
        Assert.True(Follow(rig, follower, V(0f, 450f, 0f), 15f, trace), string.Join("\n", trace));
        Assert.True(follower.LinksDone > 0, string.Join("\n", trace));
        Assert.Equal(0, follower.LinksFailed);
    }

    [Fact]
    public void AJumpNeedsHeadroomAndASupportedLanding()
    {
        var level = new CollisionDataBuilder();
        level.AddBox(V(-300f, -700f, -64f), V(300f, 0f, 0f));
        level.AddBox(V(-300f, 110f, -64f), V(300f, 700f, 0f));
        level.AddBox(V(-300f, -700f, 200f), V(300f, 700f, 230f));
        NavGraph graph = NavBuilder.Build(CollisionWorld.Build(level.Build()), V(-200f, -500f, -100f),
            V(200f, 600f, 210f), NavProfile.Spy);
        Assert.Equal(0, graph.CountLinks(NavEdgeKind.Jump));
    }

    [Theory]
    [InlineData(PawnTeam.Spy)]
    [InlineData(PawnTeam.Merc)]
    public void FollowerJumpsOntoALowPlatformWithoutInventingWalkableSteps(PawnTeam team)
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-600f, 0f, 0f), V(600f, 700f, 48f));
        PawnRig rig = Rig(level, null, V(0f, -250f, 0f), team);
        AiWorld world = World(rig, V(-250f, -500f, -100f), V(250f, 600f, 400f));
        var follower = new NavFollower(world, team == PawnTeam.Spy);
        Assert.True(follower.Graph.CountLinks(NavEdgeKind.Jump) > 0);
        var trace = new List<string>();
        Assert.True(Follow(rig, follower, V(0f, 350f, 48f), 15f, trace), string.Join("\n", trace));
        Assert.True(follower.LinksDone > 0, string.Join("\n", trace));
        Assert.Equal(0, follower.LinksFailed);
    }

    [Fact]
    public void AJumpCannotLandOnAShelfWithNoRunout()
    {
        var level = new CollisionDataBuilder();
        level.AddBox(V(-300f, -700f, -64f), V(300f, 0f, 0f));
        level.AddBox(V(-300f, 110f, -64f), V(300f, 200f, 0f));
        NavGraph graph = NavBuilder.Build(CollisionWorld.Build(level.Build()), V(-200f, -500f, -100f),
            V(200f, 250f, 400f), NavProfile.Spy);
        Assert.DoesNotContain(graph.Links, link => link.Kind == NavEdgeKind.Jump && link.Start.Y < 0f && link.End.Y > 0f);
    }

    [Fact]
    public void SpyVaultsARailWhileMercCannotPlanTheSpyMove()
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-600f, 100f, 0f), V(600f, 120f, 110f));
        TraversalDoc ges = Doc(Ge(0, GEKind.LG, V(-600f, 100f, 110f), V(600f, 100f, 110f)));
        PawnRig rig = Rig(level, ges, V(0f, -160f, 0f));
        AiWorld world = World(rig, V(-300f, -350f, -100f), V(300f, 600f, 400f), ges);
        Assert.True(world.SpyNav.CountLinks(NavEdgeKind.BarrierHop) > 0);
        Assert.Equal(0, world.MercNav.CountLinks(NavEdgeKind.BarrierHop));
        var follower = new NavFollower(world, true);
        var trace = new List<string>();
        Assert.True(Follow(rig, follower, V(0f, 400f, 0f), 18f, trace), string.Join("\n", trace));
        Assert.True(follower.LinksDone > 0);
    }

    [Fact]
    public void LongLedgeFindsAShortUsableSectionBetweenTheOldSixSamples()
    {
        CollisionDataBuilder level = Floor();
        level.AddBox(V(-240f, 100f, 0f), V(-40f, 600f, 250f));
        TraversalDoc ges = Doc(Ge(0, GEKind.LG, V(-1600f, 100f, 250f), V(1600f, 100f, 250f)));
        NavGraph graph = NavBuilder.Build(CollisionWorld.Build(level.Build()), V(-1700f, -200f, -100f),
            V(1700f, 700f, 450f), NavProfile.Spy, ges.Ges);
        Assert.Contains(graph.Links, link => link.Kind == NavEdgeKind.LedgeClimb && link.Start.X > -240f && link.Start.X < -40f);
    }

    [Fact]
    public void LongFenceKeepsSamplingBeyondTheOldFourCrossings()
    {
        CollisionDataBuilder level = Floor();
        ushort material = level.AddMaterial(0, OpenScct.Content.Collision.CollisionMaterialFlags.Climbable);
        level.AddBox(V(-1600f, 100f, 0f), V(1600f, 102f, 400f), material);
        TraversalDoc ges = Doc(Ge(0, GEKind.FENCE, V(-1600f, 100f, 400f), V(1600f, 100f, 400f)));
        NavGraph graph = NavBuilder.Build(CollisionWorld.Build(level.Build()), V(-1700f, -100f, -100f),
            V(1700f, 350f, 600f), NavProfile.Spy, ges.Ges);
        Assert.True(graph.CountLinks(NavEdgeKind.FenceClimb) > 8);
        float previous = float.MinValue;
        foreach (NavLink link in graph.Links)
        {
            if (link.Kind != NavEdgeKind.FenceClimb || link.Start.Y > 100f) continue;
            if (previous != float.MinValue) Assert.InRange(link.Start.X - previous, 1f, 96.1f);
            previous = link.Start.X;
        }
    }

    [Fact]
    public void PipeTransfersToABarAndDropsAtTheFarEnd()
    {
        var level = new CollisionDataBuilder();
        level.AddBox(V(-250f, -400f, -64f), V(250f, 200f, 0f));
        level.AddBox(V(-250f, 600f, 236f), V(250f, 1500f, 300f));
        TraversalDoc ges = Doc(Ge(0, GEKind.PIPE, V(0f, 0f, 0f), V(0f, 0f, 800f), V(0f, -10f, 0f)),
            Ge(1, GEKind.HOH, V(0f, -37f, 700f), V(0f, 1000f, 700f)));
        PawnRig rig = Rig(level, ges, V(0f, -160f, 0f));
        AiWorld world = World(rig, V(-280f, -400f, -100f), V(280f, 1500f, 1000f), ges);
        Assert.Contains(world.SpyNav.Links, link => link.Kind == NavEdgeKind.PipeToBar);
        var follower = new NavFollower(world, true);
        var trace = new List<string>();
        Assert.True(Follow(rig, follower, V(0f, 1250f, 300f), 35f, trace), string.Join("\n", trace));
        Assert.Equal(1, follower.LinksDone);
        Assert.Equal(0, follower.LinksFailed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChainsRejectInactiveOrObstructedBars(bool obstructed)
    {
        CollisionDataBuilder level = Floor();
        if (obstructed) level.AddBox(V(-200f, 400f, 450f), V(200f, 500f, 750f));
        GERecord pipe = Ge(0, GEKind.PIPE, V(0f, 0f, 0f), V(0f, 0f, 800f), V(0f, -10f, 0f));
        var bar = new GERecord(1, "bar", GEKind.HOH, [V(0f, -37f, 700f), V(0f, 1000f, 700f)],
            obstructed ? (byte)0 : (byte)2, GEPlatform.Common, GESource.Synthetic("test"));
        NavGraph graph = NavBuilder.Build(CollisionWorld.Build(level.Build()), V(-250f, -300f, -100f),
            V(250f, 1400f, 1000f), NavProfile.Spy, [pipe, bar], activationMask: 1);
        Assert.DoesNotContain(graph.Links, link => link.Kind is NavEdgeKind.PipeToBar or NavEdgeKind.PipeToBarToZip);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BarContinuesOntoZipWithoutAnIntermediateFloor(bool pipeEntry)
    {
        var level = new CollisionDataBuilder();
        float barZ = pipeEntry ? 700f : 1000f;
        float startZ = pipeEntry ? 0f : 700f;
        level.AddBox(V(-250f, -400f, startZ - 64f), V(250f, 250f, startZ));
        level.AddBox(V(-250f, 1700f, -64f), V(250f, 2600f, 0f));
        TraversalDoc ges = pipeEntry
            ? Doc(Ge(0, GEKind.PIPE, V(0f, 0f, 0f), V(0f, 0f, 800f), V(0f, -10f, 0f)),
                Ge(1, GEKind.HOH, V(0f, -37f, barZ), V(0f, 900f, barZ)),
                Ge(2, GEKind.ZL, V(0f, 900f, barZ), V(0f, 2200f, 300f)))
            : Doc(Ge(1, GEKind.HOH, V(0f, -37f, barZ), V(0f, 900f, barZ)),
                Ge(2, GEKind.ZL, V(0f, 900f, barZ), V(0f, 2200f, 300f)));
        PawnRig rig = Rig(level, ges, V(0f, -160f, startZ));
        AiWorld world = World(rig, V(-280f, -400f, -100f), V(280f, 2600f, 1200f), ges);
        NavEdgeKind kind = pipeEntry ? NavEdgeKind.PipeToBarToZip : NavEdgeKind.BarToZip;
        Assert.Contains(world.SpyNav.Links, link => link.Kind == kind);
        using var stream = new MemoryStream();
        world.SpyNav.Write(stream, "chains");
        stream.Position = 0;
        NavGraph restored = Assert.IsType<NavGraph>(NavGraph.Read(stream, "chains", NavProfile.Spy));
        Assert.Equal(world.SpyNav.ContentDigest(), restored.ContentDigest());
        var follower = new NavFollower(world, true);
        var trace = new List<string>();
        Assert.True(Follow(rig, follower, V(0f, 2350f, 0f), 40f, trace), string.Join("\n", trace));
        Assert.True(follower.LinksDone > 0, string.Join("\n", trace));
        Assert.Equal(0, follower.LinksFailed);
    }
}
