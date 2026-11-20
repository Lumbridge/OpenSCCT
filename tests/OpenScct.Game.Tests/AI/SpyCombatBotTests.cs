using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class SpyCombatBotTests
{
    [Fact]
    public void SpyBotSticksItsQuickCamBesideANearbyMercAndPutsHimToSleep()
    {
        var collision = Floor();
        collision.AddBox(V(-500, 540, 0), V(500, 580, 340));
        var rig = Rig(collision, null, V(0, 0, 0));
        CombatEquipment.Install(rig.Level);
        var merc = rig.AddMate(PawnTeam.Merc, V(55, 350, 0), 16384);
        var world = World(rig, V(-500, -500, -100), V(500, 590, 350));
        var pistol = rig.Level.SpawnActor(new SStickyShocker(), rig.Pawn.Location, default)!;
        pistol.GiveTo(rig.Pawn);
        var cam = rig.Level.SpawnActor(new SGadStickyCam(), rig.Pawn.Location, default)!;
        cam.GiveTo(rig.Pawn);
        rig.Controller.Player = new AiViewport();
        rig.Level.Info.LocalPlayerController = null;
        var bot = new AiController(world, rig.Controller, AiDifficulty.For(AiSkill.Expert) with { MeleeUse = 0f }, 0);

        for (int frame = 0; frame < 20 * Sim.Hz && !merc.Pawn.bSleeping; frame++)
        {
            world.BeginTick();
            bot.Tick();
            rig.World.Step();
        }

        Assert.True(bot.Gadgets.CamsPlaced > 0, $"The spy never placed its quick cam. {bot.Status}, {rig.Controller.GetStateName()}");
        Assert.True(bot.Gadgets.Gassings > 0, $"The spy never released gas. {bot.Status}, {rig.Controller.GetStateName()}");
        Assert.True(merc.Pawn.bSleeping, $"The gas missed its target. {merc.Controller.GetStateName()}");
    }

    [Fact]
    public void SpyBotGrabsAnExposedBackBeforeTheOpeningCloses()
    {
        var rig = Rig(Floor(), null, V(0, 0, 0));
        CombatEquipment.Install(rig.Level);
        var merc = rig.AddMate(PawnTeam.Merc, V(0, 130, 0), 16384);
        merc.Pawn.iLastWakeTime = -100;
        merc.Controller.TimeShockForGrab = -100f;
        var world = World(rig, V(-500, -500, -100), V(500, 500, 350));
        rig.Controller.Player = new AiViewport();
        rig.Level.Info.LocalPlayerController = null;
        var bot = new AiController(world, rig.Controller, AiDifficulty.For(AiSkill.Hard) with { GadgetUse = 0f, DecisionNoise = 0f }, 0);
        var trace = new System.Text.StringBuilder();

        for (int frame = 0; frame < 1.35f * Sim.Hz; frame++)
        {
            world.BeginTick();
            bot.Tick();
            rig.World.Step();
            if (frame % 8 == 0)
                trace.AppendLine($"{frame}: {bot.Status} {rig.Controller.GetStateName()}, {rig.Pawn.Location}/{merc.Pawn.Location}, facing {rig.Pawn.Rotation}/{merc.Pawn.Rotation}, see {bot.Memory.Find(merc.Pawn)?.BodyVisible}, grab {rig.Controller.DescribeGrab(merc.Pawn)}");
        }

        var brain = Assert.IsType<SpyAi>(bot.Brain);
        Assert.True(brain.GrabPresses > 0, $"The spy missed the close grab window. {trace}");
        for (int frame = 0; frame < 8 * Sim.Hz && merc.Pawn.Health > 0; frame++)
        {
            world.BeginTick();
            bot.Tick();
            rig.World.Step();
            if (frame % 24 == 0)
                trace.AppendLine($"+{frame}: {bot.Status} {rig.Controller.GetStateName()}/{merc.Controller.GetStateName()}, {rig.Pawn.Location}/{merc.Pawn.Location}, see {bot.Memory.Find(merc.Pawn)?.BodyVisible}, grab {rig.Controller.DescribeGrab(merc.Pawn)}, enabled {rig.Controller.GameReplicationInfo?.bAvlb_SPY_Grab}, use {bot.LastInput.bUsing}, shock {merc.Controller.TimeShockForGrab}/{world.Now}");
        }

        Assert.True(brain.Grabs > 0, $"The grab was never accepted. {trace}");
        Assert.True(merc.Pawn.Health <= 0, $"The spy did not finish the neck grab. {merc.Controller.GetStateName()}");
    }
}
