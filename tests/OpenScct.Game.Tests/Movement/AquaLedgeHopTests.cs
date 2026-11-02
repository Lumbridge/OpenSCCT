using System.Collections.Generic;
using System.IO;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

public sealed class AquaLedgeHopTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(240)]
    public void ExteriorFeetHopRestoresTheStandingPoseAndMovement(int hz)
    {
        string folder = Path.Combine(RepoRoot, ".content", "svmap", "AquaE");
        Assert.SkipWhen(!Directory.Exists(folder), "AquaE has not been converted into .content/svmap.");
        PawnRig rig = PawnRig.ForMap(PawnTeam.Spy, folder);
        // Approach the low balcony ledge whose far edge supports the reported frozen position
        // (5203.1973, 3996.2073, 697.005). The balcony floor is 64 uu below the ledge top.
        rig.Place(V(5203.1973f, 3860f, 633.005f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
        rig.SetFastSpeed(true);
        Assert.True(rig.WaitStill(120));
        var seen = new List<Name>();
        bool climbing = false;
        bool completed = false;
        for (int tick = 0; tick < hz * 4; tick++)
        {
            rig.Step(new PlayerInputFrame { Jump = tick == hz / 3, BT_Up = climbing ? (byte)0 : (byte)1 }, 1f / hz);
            Name state = rig.Controller.GetStateName();
            if (seen.Count == 0 || seen[^1] != state) seen.Add(state);
            climbing |= state == MovementStateNames.s_FeetLedgeDetect || state == MovementStateNames.s_FeetLedge;
            if (climbing && state == MovementStateNames.s_PlayerWalkingSt0H)
            {
                completed = true;
                break;
            }
        }

        Assert.Contains(MovementStateNames.s_FeetLedgeDetect, seen);
        Assert.Contains(MovementStateNames.s_FeetLedge, seen);
        Assert.True(completed, string.Join(", ", seen));
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.Equal(rig.Pawn.AWaitSt0, rig.Pawn.PawnAnim[0].Sequence);
        Assert.True(rig.Pawn.PawnAnim[0].bLoop);
        Assert.True(rig.Pawn.PawnAnim[0].bPlaying);
        Assert.Equal(0, rig.Pawn.NetworkRootMotion.RootLockType);
        Assert.InRange(rig.Pawn.Location.Y, 3968f, 4003f);
        Assert.InRange(rig.Pawn.Location.Z, 695f, 698f);

        FVector landed = rig.Pawn.Location;
        for (int tick = 0; tick < hz / 2; tick++)
            rig.Step(new PlayerInputFrame { BT_Up = 1 }, 1f / hz);

        Assert.True(rig.Pawn.Location.Y > landed.Y + 50f, $"The spy did not move away from {landed}: {rig.Pawn.Location}");
        Assert.True(rig.Pawn.Location.Z < landed.Z - 20f, $"The spy did not fall from the ledge: {rig.Pawn.Location}");
    }
}
