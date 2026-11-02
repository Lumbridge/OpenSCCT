using OpenScct.Core;
using OpenScct.Game.AI;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.AI.AiTestKit;

namespace OpenScct.Game.Tests.AI;

public sealed class MercTrapTests
{
    [Theory]
    [InlineData(false, 0f)]
    [InlineData(true, 0f)]
    [InlineData(false, -200f)]
    [InlineData(true, -200f)]
    public void GuardUsesCarriedTrapAtAWallBesideAnObjective(bool tracker, float startX)
    {
        var geometry = Floor();
        geometry.AddBox(V(45, -1000, 0), V(85, 1000, 300));
        geometry.AddBox(V(-500, -1000, 0), V(-460, 1000, 300));
        var rig = Rig(geometry, null, V(startX, 0, 0), PawnTeam.Merc, yaw: 0);
        OpenScct.Game.Equipment.PawnGadgets.RegisterNotifies(rig.Level);
        var world = World(rig, V(-400, -640, -100), V(0, 640, 400));
        rig.Level.SpawnActor(new SComputerObjectiveTrigger { bStatic = false, bNoDelete = false,
            bCollideActors = false, bBlockPlayers = false }, V(-150, 150, 90), default);
        SGadMultiMinesLauncher launcher = tracker ? new SGadMultiPresDetectorLauncher() : new SGadMultiMinesLauncher();
        rig.Level.SpawnActor(launcher, rig.Pawn.Location, default);
        launcher.GiveTo(rig.Pawn);
        launcher.Charge = 3;
        rig.Pawn.SelectedGadget = launcher;
        var bot = new AiController(world, rig.Controller, AiDifficulty.For(AiSkill.Normal), 0);
        bot.Tick();
        if (startX == 0f) Assert.Equal(new Name("s_PutMineOnWall_Grab"), rig.Controller.GetStateName());
        Assert.Equal(3, launcher.Charge); // Starting the clip must not bypass the placement notify.
        for (int i = 0; i < 4 * Sim.Hz; i++)
        {
            bot.Tick();
            rig.World.Step();
        }
        Assert.True(launcher.Charge == 2, $"state {rig.Controller.GetStateName()} anim {rig.Pawn.PawnAnim[SBasePawn.INTERACTCHANNEL].Sequence} charge {launcher.Charge}");
        Assert.Equal(1, tracker ? rig.Controller.NbTrapMineInMapIOwn : rig.Controller.NbBasicMineInMapIOwn);
    }
}
