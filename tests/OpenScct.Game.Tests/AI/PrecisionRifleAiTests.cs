using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.AI;
using OpenScct.Game.Devices;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.Svm;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class PrecisionRifleAiTests
{
    [Theory]
    [InlineData("Svm.BoltRifle", 6)]
    [InlineData("Svm.DesignatedMarksman", 4)]
    public void ExpertMercUsesItsRifleScopeAndReleasesBetweenShots(string weapon, int magnification)
    {
        var rig = PawnRig.Create(PawnTeam.Merc, Floor().Build(), options: new PawnRigOptions { NetMode = ENetMode.NM_DedicatedServer });
        var facing = new FRotator(0, 16384, 0);
        rig.Place(V(0, 0, 89.5f), facing, facing, crouched: false);
        rig.Controller.Player = new AiViewport();
        rig.Level.Info.LocalPlayerController = null;
        CombatEquipment.Install(rig.Level);
        var world = World(rig, V(-300, -300, -100), V(300, 2400, 350));
        var rifle = Assert.IsAssignableFrom<PrecisionRifle>(DeviceClasses.Spawn(rig.Pawn, new ClassRef(weapon),
            rig.Pawn, default, rig.Pawn.Location, facing));
        rifle.GiveTo(rig.Pawn);
        rig.Pawn.WeaponLoaded = 2;
        var spy = rig.AddMate(PawnTeam.Spy, V(0, 2000, 0), -16384);
        spy.Pawn.Health = 1000; // Keep the same target alive for three successive shots.
        rig.World.Step(2 * Sim.Hz);
        var bot = new AiController(world, rig.Controller, AiDifficulty.For(AiSkill.Expert), 0);
        int shots = 0;
        int lastShot = -1;
        bool released = true;
        for (int frame = 0; frame < Sim.Hz * 8 && shots < 3; frame++)
        {
            int previousAmmo = rifle.ClipAmmo;
            bot.Tick();
            rig.World.Step();
            if (rifle.ClipAmmo == previousAmmo - 1)
            {
                Assert.True(rifle.AimRequested);
                Assert.False(rig.Controller.bBinocularVision);
                Assert.Equal(magnification, WeaponAttachments.OpticPower(rifle));
                Assert.True(released);
                Assert.Equal(1, bot.LastInput.bFire);
                Assert.True(bot.LastInput.Command1 == AiCommand.Fire || bot.LastInput.Command2 == AiCommand.Fire);
                Assert.True(lastShot < 0 || (frame - lastShot) * Sim.DeltaTime + .0001f >= rifle.ShotInterval);
                lastShot = frame;
                released = false;
                shots++;
            }
            else if (bot.LastInput.bFire == 0) released = true;
        }
        Assert.True(shots == 3, $"{shots} accepted shots. {bot.Status}. {rig.Controller.GetStateName()}.");
        Assert.True(spy.Pawn.Health < 1000);
    }
}
