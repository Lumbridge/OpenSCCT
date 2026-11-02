using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class MercBombTests
{
    [Fact]
    public void MercRemovesAnArmedBombThroughTheUseMenu()
    {
        var rig = Rig(Floor(), null, V(0, 0, 0), PawnTeam.Merc);
        var world = World(rig, V(-640, -640, -100), V(640, 960, 400));
        rig.Controller.IManager = rig.Level.SpawnActor(new SInteractionManager(), default, default, rig.Controller);
        var bomb = rig.Level.SpawnActor(new SBombTargetObjectiveTrigger { bStatic = false, bNoDelete = false }, V(0, 80, 90), default)!;
        bomb.bActive = true;
        bomb.CaptureTime = 60;
        bomb.InteractionObj!.bUsable = true;
        bomb.eventTriggering(true);
        bomb.InteractionObj.DoTouch(rig.Pawn);
        var bot = new AiController(world, rig.Controller, AiDifficulty.For(AiSkill.Normal), 0);
        for (int i = 0; i < 5 * Sim.Hz && bomb.bTriggering; i++)
        {
            bot.Tick();
            rig.World.Step();
        }
        Assert.False(bomb.bTriggering, $"state {rig.Controller.GetStateName()}, goal {bot.Brain?.Goal}, at {rig.Pawn.Location}");
    }

    [Theory]
    [InlineData(600f, MercAi.GDefuse)]
    [InlineData(150f, MercAi.GEngage)]
    public void ArmedBombOverridesChasingButAllowsImmediateSelfDefense(float spyDistance, int expected)
    {
        var rig = Rig(Floor(), null, V(0, 0, 0), PawnTeam.Merc);
        var world = World(rig, V(-640, -640, -100), V(640, 960, 400));
        var bot = new AiController(world, rig.Controller, AiDifficulty.For(AiSkill.Normal), 0);
        bot.Tick();
        var bomb = rig.Level.SpawnActor(new SBombTargetObjectiveTrigger { bStatic = false, bNoDelete = false }, V(0, 800, 100), default)!;
        bomb.bActive = true;
        bomb.bTriggering = true;
        var spy = rig.Level.SpawnActor(new SPawnAttaque(), V(0, spyDistance, 90), default)!;
        bot.Memory.Observe(spy, new AiSighting(true, 1f, spyDistance, 1f, true), world.Now, 2f, bot.Difficulty);
        var plan = new AiPlan();
        bot.Brain!.Think(world.Now + 1f, ref plan);
        Assert.Equal(expected, bot.Brain.Goal);
    }
}
