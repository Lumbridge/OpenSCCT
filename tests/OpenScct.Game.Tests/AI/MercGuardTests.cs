using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class MercGuardTests
{
    [Fact]
    public void GuardsADeskTerminalFromReachableFloor()
    {
        var level = Floor();
        level.AddBox(V(-100f, 400f, 0f), V(100f, 600f, 80f));
        PawnRig rig = Rig(level, null, V(0f, -400f, 0f), PawnTeam.Merc);
        AiWorld world = World(rig, V(-640f, -640f, -100f), V(640f, 960f, 400f));
        SComputerObjectiveTrigger terminal = Terminal(rig, V(0f, 500f, 80f), "Desk");
        var path = new List<NavStep>();
        int start = world.MercNav.NearestNode(rig.Pawn.Location, rig.Pawn.CollisionHeight);
        int desk = world.MercNav.NearestNode(terminal.Location);
        Assert.False(world.MercNav.FindPath(start, desk, path));

        var bot = new AiController(world, rig.Controller, AiDifficulty.For(AiSkill.Normal), 0);
        bot.Tick();
        NavFollower follower = Assert.IsType<MercAi>(bot.Brain).Follower;
        Assert.True(follower.HasGoal);
        Assert.InRange(follower.Goal.Z, -1f, 1f);
        Assert.InRange(NavFollower.Horizontal(follower.Goal - terminal.Location), 150f, 300f);
        Assert.True(Follow(rig, follower, follower.Goal, 12f));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DoesNotChooseAGuardPostThroughAWall(bool alternate)
    {
        var level = Floor();
        level.AddBox(V(-32f, -1200f, 0f), V(32f, 1200f, 300f));
        PawnRig rig = Rig(level, null, V(-320f, 0f, 0f), PawnTeam.Merc);
        AiWorld world = World(rig, V(-960f, -960f, -100f), V(640f, 960f, 400f));
        Terminal(rig, V(128f, 0f, 80f), "Blocked");
        if (alternate)
        {
            Terminal(rig, V(-600f, 600f, 80f), "Reachable");
        }

        var bot = new AiController(world, rig.Controller, AiDifficulty.For(AiSkill.Normal), 0);
        bot.Tick();
        MercAi brain = Assert.IsType<MercAi>(bot.Brain);
        if (alternate)
        {
            Assert.Contains("post Reachable", brain.Status);
            Assert.True(brain.Follower.HasGoal);
            Assert.True(Follow(rig, brain.Follower, brain.Follower.Goal, 12f));
        }
        else
        {
            Assert.False(brain.Follower.HasGoal);
            Assert.Contains("post -", brain.Status);
        }
    }

    private static SComputerObjectiveTrigger Terminal(PawnRig rig, FVector at, string name) =>
        rig.Level.SpawnActor(new SComputerObjectiveTrigger
        {
            TriggerName = name,
            bStatic = false,
            bNoDelete = false,
            UseRelativeLocation = default,
            bCollideActors = false,
            bBlockPlayers = false,
        }, at, default)!;
}
