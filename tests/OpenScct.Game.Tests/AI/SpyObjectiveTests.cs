using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class SpyObjectiveTests
{
    private static SDisk Disk(PawnRig rig, FVector at)
    {
        SDisk disk = rig.Level.SpawnActor(new SDisk { bStatic = false, bNoDelete = false }, at, default)!;
        disk.PickupInteraction ??= rig.Level.SpawnActor(new SPickupInteraction { bStatic = false, bNoDelete = false }, at, default, disk);
        disk.Reset();
        return disk;
    }

    private static AiController Bot(PawnRig rig, AiWorld world) => new(world, rig.Controller,
        AiDifficulty.For(AiSkill.Normal) with { DecisionNoise = 0f, GadgetUse = 0f }, 0);

    [Fact]
    public void BotWalksToDiskAndPicksItUpThroughUseMenu()
    {
        var rig = Rig(Floor(), null, V(0, -200, 0));
        var world = World(rig, V(-320, -320, -100), V(320, 320, 300));
        SDisk disk = Disk(rig, V(0, 100, 20));
        rig.Controller.Player = new AiViewport();
        rig.Level.Info.LocalPlayerController = null;
        var bot = Bot(rig, world);
        for (int frame = 0; frame < 20 * Sim.Hz && disk.bActive; frame++)
        {
            bot.Tick();
            rig.World.Step();
        }
        Assert.False(disk.bActive, $"{bot.Status}; state {rig.Controller.GetStateName()}; at {rig.Pawn.Location}; menu {rig.Controller.IManager?.GetCurrentInteraction()?.GetType().Name}");
        Assert.Contains(disk, rig.Pawn.Disks);
    }

    [Fact]
    public void ServerBotPickupUsesItsOwnMenuWithoutBeingLocalPlayer()
    {
        var rig = Rig(Floor(), null, V(0, 0, 0));
        SDisk disk = Disk(rig, V(20, 0, 20));
        rig.Controller.Player = new AiViewport();
        rig.Level.Info.LocalPlayerController = null;
        rig.Controller.IManager ??= rig.Level.SpawnActor(new SInteractionManager(), default, default, rig.Controller);
        rig.Controller.IManager!.Interactions[0] = disk.PickupInteraction;
        rig.Controller.IManager.NbObjects = 1;
        rig.Controller.IManager.SelectedInteractions = 0;
        rig.Controller.GotoState(new Name("s_Pickup"));
        Assert.Contains(disk, rig.Pawn.Disks);
        Assert.False(disk.bActive);
    }

    [Fact]
    public void ChoosesReachableDiskInsteadOfDiskOnInaccessibleTower()
    {
        var collision = Floor();
        collision.AddBox(V(-100, -100, 0), V(100, 100, 500));
        var rig = Rig(collision, null, V(0, -300, 0));
        var world = World(rig, V(-640, -640, -100), V(640, 640, 800));
        SDisk tower = Disk(rig, V(0, 0, 520));
        SDisk reachable = Disk(rig, V(400, 400, 20));
        var bot = Bot(rig, world);
        bot.Tick();
        var brain = Assert.IsType<SpyAi>(bot.Brain);
        Assert.Equal(SpyAi.GObjective, brain.Goal);
        Assert.Same(reachable, brain.OtherObjectiveTarget);
        Assert.True(brain.Follower.HasGoal);
        Assert.InRange(brain.Follower.Goal.Z, -1, 1);
        Assert.NotSame(tower, brain.OtherObjectiveTarget);
    }

    [Fact]
    public void LockedTerminalIsNotChosenOverAvailableDisk()
    {
        var rig = Rig(Floor(), null, V(0, -200, 0));
        var world = World(rig, V(-320, -320, -100), V(320, 320, 300));
        rig.Level.SpawnActor(new SComputerObjectiveTrigger { bStatic = false, bNoDelete = false, bLocked = true }, V(0, 100, 0), default);
        SDisk disk = Disk(rig, V(200, 100, 20));
        var bot = Bot(rig, world);
        bot.Tick();
        var brain = Assert.IsType<SpyAi>(bot.Brain);
        Assert.Equal(SpyAi.GObjective, brain.Goal);
        Assert.Same(disk, brain.OtherObjectiveTarget);
    }

    [Fact]
    public void BombIsAlternativeToUnreachableDiskWhenSpyHasModem()
    {
        var collision = Floor();
        collision.AddBox(V(-100, -100, 0), V(100, 100, 500));
        var rig = Rig(collision, null, V(0, -300, 0));
        var world = World(rig, V(-640, -640, -100), V(640, 640, 800));
        Disk(rig, V(0, 0, 520));
        var bomb = rig.Level.SpawnActor(new SBombTargetObjectiveTrigger { bStatic = false, bNoDelete = false }, V(400, 400, 40), default)!;
        bomb.InteractionObj ??= rig.Level.SpawnActor(new SBombTargetInteraction { bStatic = false, bNoDelete = false }, bomb.Location, default, bomb);
        bomb.Reset();
        var modem = rig.Level.SpawnActor(new SGadBomb(), rig.Pawn.Location, default)!;
        modem.GiveTo(rig.Pawn);
        var bot = Bot(rig, world);
        bot.Tick();
        var brain = Assert.IsType<SpyAi>(bot.Brain);
        Assert.Equal(SpyAi.GObjective, brain.Goal);
        Assert.Same(bomb, brain.OtherObjectiveTarget);
        rig.Controller.Player = new AiViewport();
        rig.Level.Info.LocalPlayerController = null;
        var trace = new System.Text.StringBuilder();
        for (int frame = 0; frame < 20 * Sim.Hz && !bomb.bTriggering; frame++)
        {
            bot.Tick();
            rig.World.Step();
            if (frame % 60 == 0) trace.AppendLine($"{frame}: {bot.Status}; {rig.Controller.GetStateName()} at {rig.Pawn.Location}");
        }
        Assert.True(bomb.bTriggering, trace.ToString() + $"{bot.Status}; state {rig.Controller.GetStateName()}; at {rig.Pawn.Location}; menu {rig.Controller.IManager?.GetCurrentInteraction()?.GetType().Name}");

    }
}
