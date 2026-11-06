using System.Collections.Generic;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class RouteAiTests
{
    [Theory]
    [InlineData(EPhysics.PHYS_RootMotion)]
    [InlineData(EPhysics.PHYS_Falling)]
    public void ArrivalWaitsUntilTheWalkerLands(EPhysics physics)
    {
        var rig = Rig(Floor(), null, V(0, 0, 0));
        var world = World(rig, V(-160, -160, -64), V(320, 160, 300));
        var reports = new List<string>();
        var route = new AiRoute([new("roof", V(0, 0, 0), null), new("next", V(200, 0, 0), null)], reports.Add);
        var bot = new AiController(world, rig.Controller, AiDifficulty.For(AiSkill.Normal), 0) { Route = route };
        rig.Pawn.Physics = physics;
        bot.Tick();
        var brain = Assert.IsType<RouteAi>(bot.Brain);
        Assert.Equal(0, brain.Reached);
        Assert.False(brain.Finished);
        Assert.DoesNotContain(reports, line => line.StartsWith("reached"));

        rig.Pawn.Physics = EPhysics.PHYS_Walking;
        bot.Tick();
        Assert.Equal(1, brain.Reached);
        bot.Tick();
        Assert.False(brain.Finished);
        Assert.DoesNotContain(reports, line => line.StartsWith("stuck"));
    }
}
