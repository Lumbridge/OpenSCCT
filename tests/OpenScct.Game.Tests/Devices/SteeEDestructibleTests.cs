using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.AI;
using OpenScct.Game.Tests.Game;
using Xunit;

namespace OpenScct.Game.Tests.Devices;

/// <summary>The authored props reported near the courtyard and molotov shelf, using the converted SteeE map.</summary>
public sealed class SteeEDestructibleTests
{
    [Fact]
    public void DelayedMolotovFireCreditsTheBottleShooterForAnotherPlayersDeath()
    {
        (GameWorld world, SPlayerController[] players) = DeathLivesRespawnTests.Match(
            MatchLifecycleTests.SteeEStory, "?Name=Victim?Team=0", "?Name=Shooter?Team=1");
        world.Run(5f);
        SPlayerController victimController = players[0];
        SPlayerController shooter = players[1];
        Pawn victim = victimController.Pawn!;
        victim.Health = 60;

        // The same sender/relay/volume chain as SteeE, without the bottle's initial radius blast.
        var bottle = new SGamePlayObject { bNoDelete = false, bDamageable = true, bCanDamageActor = false, HitPoints = 10 };
        bottle.DamagedMeshes.Add(new FDamagedMesh { Percent = 100, Event = "molotov" });
        Assert.Same(bottle, world.Level.SpawnActor(bottle, default, default));
        var relay = new SMagicEvent { bNoDelete = false, Tag = "molotov" };
        var group = new FEventGroup();
        group.EventGroup.Add(new FSuperEvent { Delay = 1f, Event = "moloaie" });
        relay.Groups.Add(group);
        Assert.Same(relay, world.Level.SpawnActor(relay, default, default));
        var fire = new SDamageVolume { bNoDelete = false, Tag = "moloaie", bToggle = true, HitPoints = 30 };
        Assert.Same(fire, world.Level.SpawnActor(fire, victim.Location, default));
        fire.BeginTouch(victim);
        bottle.eventTakeDamage(10, shooter.Pawn, bottle.Location, new FVector(1f, 0f, 0f), SDamageType.SDTBase);
        Assert.Null(world.Game.GPODamageInstigator);
        Assert.False(fire.bActive);

        world.Run(1.1f);
        Assert.Equal(30, victim.Health);
        Assert.Same(shooter, fire.TriggerController);
        world.Run(1.05f);
        Assert.True(victim.Health <= 0);
        Assert.Equal(1, victimController.PlayerReplicationInfo!.Deaths);
        Assert.Equal(3, victimController.PlayerReplicationInfo.NumLives);
        Assert.Equal(1, shooter.PlayerReplicationInfo!.Kills);
        Assert.Equal(1, shooter.PlayerReplicationInfo.Stats!.EnvironmentalKills);
        Assert.Equal(60, shooter.PlayerReplicationInfo.Stats.DamageGiven);
        Assert.Equal((float)world.Level.Info.GetActionPoints(LevelInfo.AP_D_KillWithWeapon), shooter.PlayerReplicationInfo.Score);
        world.Run(0.25f);
        Assert.Equal(1, victimController.PlayerReplicationInfo.Deaths);
    }

    [Fact]
    public void BrokenMolotovEnablesItsFireDamagesAPlayerAndEndsAfterTheAuthoredDelay()
    {
        string? folder = AiTestKit.SteeEFolder;
        Assert.SkipWhen(folder is null, "SteeE has not been converted into .content/svmap.");
        PawnRig rig = PawnRig.ForMap(PawnTeam.Merc, folder!);
        SGamePlayObject bottle = Assert.IsType<SGamePlayObject>(rig.Map!.FindActor("SGamePlayObject215"));
        SDamageVolume fire = Assert.IsType<SDamageVolume>(rig.Map.FindActor("SDamageVolume228"));
        SMagicEvent relay = Assert.IsType<SMagicEvent>(rig.Map.FindActor("SMagicEvent232"));

        // Keep the shooter outside the blast while the map's delayed events advance.
        rig.Pawn.Physics = EPhysics.PHYS_None;
        rig.Controller.GotoState("TestNoState");
        rig.Pawn.Location = new FVector(-1400f, -1152f, -719f);
        rig.World.Step(120);
        Assert.False(fire.bActive);
        bottle.eventTakeDamage(10, rig.Pawn, bottle.Location, new FVector(1f, 0f, 0f), SDamageType.SDTBase);
        Assert.True(bottle.bAlreadyDestroyed);
        Assert.Equal(EDrawType.DT_None, bottle.DrawType);
        Assert.Equal(2, relay.EventQueue.Count);
        rig.World.Step(30);
        Assert.False(fire.bActive);
        rig.World.Step(36);
        Assert.True(fire.bActive);
        Assert.Same(rig.Controller, fire.TriggerController);

        // Use the real brush overlap to enter the fire. The player's ordinary damage path must lose health.
        Assert.True(rig.Pawn.SetLocation(new FVector(-1688f, -1152f, -719f)));
        rig.Driver.EncroachTouches(rig.Pawn);
        Assert.Contains(rig.Pawn, fire.Touching);
        int health = rig.Pawn.Health;
        rig.World.Step();
        Assert.Equal(health - 30, rig.Pawn.Health);
        rig.World.Step(65);
        Assert.Equal(health - 60, rig.Pawn.Health);

        Assert.True(rig.Pawn.SetLocation(new FVector(-1400f, -1152f, -719f)));
        rig.Driver.EncroachTouches(rig.Pawn);
        Assert.DoesNotContain(rig.Pawn, fire.Touching);
        rig.World.Step(29 * 60);
        Assert.True(fire.bActive);
        rig.World.Step(60);
        Assert.False(fire.bActive);
        Assert.Null(fire.TriggerController);
        Assert.Empty(relay.EventQueue);
    }
}
