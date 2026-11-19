using System.Collections.Generic;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class SpyMovementTests
{
    [Fact]
    public void StandsAndRollsOnAClearRouteAfterSneaking()
    {
        var rig = Rig(Floor(), null, V(0f, -1000f, 0f));
        var facing = new FRotator(0, 16384, 0);
        rig.Place(rig.Pawn.Location, facing, facing, crouched: true);
        Assert.True(rig.Pawn.bIsCrouched);
        var world = World(rig, V(-350f, -1200f, -100f), V(350f, 1200f, 250f));
        var disk = rig.Level.SpawnActor(new SDisk { bStatic = false, bNoDelete = false }, V(0f, 1000f, 20f), default)!;
        disk.PickupInteraction ??= rig.Level.SpawnActor(new SPickupInteraction { bStatic = false, bNoDelete = false }, disk.Location, default, disk);
        disk.Reset();
        rig.Controller.Player = new AiViewport();
        rig.Level.Info.LocalPlayerController = null;
        var bot = new AiController(world, rig.Controller,
            AiDifficulty.For(AiSkill.Expert) with { DecisionNoise = 0f, GadgetUse = 0f }, 0);

        bool rolled = false;
        for (int tick = 0; tick < 8 * Sim.Hz && disk.bActive; tick++)
        {
            bot.Tick();
            rig.World.Step();
            rolled |= rig.Pawn.bIsRolling;
        }

        Assert.True(rolled, bot.Status);
        Assert.False(disk.bActive, bot.Status);
    }

    [Fact]
    public void TheShockWindowOnlyCoversClimbsTheSpyCanClearBeforeTheMercRecovers()
    {
        var rig = Rig(Floor(), null, default);
        var world = World(rig, V(-500f, -400f, -100f), V(500f, 500f, 600f));
        var difficulty = AiDifficulty.For(AiSkill.Normal);
        var bot = new AiController(world, rig.Controller, difficulty, 0);
        bot.Tick();
        var brain = Assert.IsType<SpyAi>(bot.Brain);
        var merc = rig.Level.SpawnActor(new SPAWNDEFENSE { Health = 100 }, V(0f, -350f, 87.5f), default)!;
        float now = world.Now;
        var shortLadder = new NavLink(NavEdgeKind.LadderUp, 0, 1, default, 16384, V(0f, 120f, 64f), 0, 64f);
        var tallLadder = shortLadder with { Height = 400f };
        var shouldDefer = brain.Follower.DeferLink!;

        Assert.False(shouldDefer(tallLadder, now));
        bot.Memory.Observe(merc, new AiSighting(true, 1f, 350f, 1f, true), now, 1f, difficulty);
        Assert.True(shouldDefer(shortLadder, now));
        brain.NoteShock(merc, now);
        Assert.False(shouldDefer(shortLadder, now));
        Assert.True(shouldDefer(tallLadder, now));
        Assert.True(shouldDefer(shortLadder, now + 1f));
    }

    [Fact]
    public void DeferringAnExposedLadderSelectsTheOtherWithoutMarkingEitherBroken()
    {
        var geometry = Floor();
        geometry.AddBox(V(-450f, 105f, 0f), V(450f, 900f, 400f));
        TraversalDoc ladders = Doc(
            Ge(0, GEKind.LADDER, V(-240f, 100f, 0f), V(-240f, 100f, 400f), V(-240f, 90f, 0f)),
            Ge(1, GEKind.LADDER, V(240f, 100f, 0f), V(240f, 100f, 400f), V(240f, 90f, 0f)));
        var rig = Rig(geometry, ladders, V(-240f, -130f, 0f));
        var world = World(rig, V(-500f, -300f, -100f), V(500f, 960f, 600f), ladders);
        bool deferred = false;
        var follower = new NavFollower(world, spy: true)
        {
            DeferLink = (link, _) => link.Kind == NavEdgeKind.LadderUp && link.Start.X < 0f && (deferred = true),
        };
        FVector goal = V(-240f, 290f, 400f);
        Assert.True(follower.SetGoal(rig.Pawn, goal));
        Assert.Contains(follower.Path, step => step.Kind == NavEdgeKind.LadderUp && follower.Graph.Links[step.Link].Start.X < 0f);

        var trace = new List<string>();
        Assert.True(Follow(rig, follower, goal, 25f, trace), string.Join("\n", trace));
        Assert.True(deferred);
        Assert.Equal(1, follower.LinksDone);
        Assert.Equal(0, follower.LinksFailed);
        Assert.Empty(world.BadLinks(spy: true));
    }

    [Theory]
    [InlineData(NavEdgeKind.LadderUp)]
    [InlineData(NavEdgeKind.LedgeClimb)]
    public void SpyClimbsToItsObjectiveWhenTheRouteIsSafe(NavEdgeKind expected)
    {
        var geometry = Floor();
        geometry.AddBox(V(-300f, 105f, 0f), V(300f, 700f, 250f));
        TraversalDoc traversal = expected == NavEdgeKind.LadderUp
            ? Doc(Ge(0, GEKind.LADDER, V(0f, 100f, 0f), V(0f, 100f, 250f), V(0f, 90f, 0f)))
            : Doc(Ge(0, GEKind.LG, V(-300f, 105f, 250f), V(300f, 105f, 250f)));
        var rig = Rig(geometry, traversal, V(0f, -200f, 0f));
        var world = World(rig, V(-400f, -300f, -100f), V(400f, 750f, 450f), traversal);
        var disk = rig.Level.SpawnActor(new SDisk { bStatic = false, bNoDelete = false }, V(0f, 440f, 270f), default)!;
        disk.PickupInteraction ??= rig.Level.SpawnActor(new SPickupInteraction { bStatic = false, bNoDelete = false }, disk.Location, default, disk);
        disk.Reset();
        rig.Controller.Player = new AiViewport();
        rig.Level.Info.LocalPlayerController = null;
        var bot = new AiController(world, rig.Controller,
            AiDifficulty.For(AiSkill.Normal) with { DecisionNoise = 0f, GadgetUse = 0f }, 0);

        bool usedLink = false;
        for (int tick = 0; tick < 20 * Sim.Hz && disk.bActive; tick++)
        {
            bot.Tick();
            rig.World.Step();
            NavFollower follower = Assert.IsType<SpyAi>(bot.Brain).Follower;
            usedLink |= follower.ActiveLink >= 0 && follower.CurrentKind == expected;
        }

        Assert.True(usedLink, bot.Status);
        Assert.True(bot.Brain!.Follower.LinksDone > 0, bot.Status);
        Assert.False(disk.bActive, bot.Status);
    }
}
