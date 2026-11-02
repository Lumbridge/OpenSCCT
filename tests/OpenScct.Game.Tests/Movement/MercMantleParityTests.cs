using System.Collections.Generic;
using System.IO;
using OpenScct.Content;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;
using static OpenScct.Game.Tests.Movement.MovesetTestKit;

namespace OpenScct.Game.Tests.Movement;

public sealed class MercMantleParityTests
{
    [Theory]
    [InlineData(30, 64)]
    [InlineData(60, 64)]
    [InlineData(120, 64)]
    [InlineData(240, 64)]
    [InlineData(30, 80)]
    [InlineData(60, 80)]
    [InlineData(120, 80)]
    [InlineData(240, 80)]
    public void LowPlatformMantleUsesTheArmedExitAndRestoresControl(int hz, int height)
    {
        var collision = Floor();
        collision.AddBox(V(-500, 100, 0), V(500, 1200, height));
        var ledges = Doc(Ge(0, GEKind.LG, V(-500, 100, height), V(500, 100, height)));
        PawnRig rig = PawnRig.Create(PawnTeam.Merc, collision.Build(), ledges, ContentOptions());
        rig.Place(V(0, -8, 89.005f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
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
            if (climbing && state == MovementStateNames.s_PlayerWalkingSt2H)
            {
                completed = true;
                break;
            }
        }

        Assert.True(completed, string.Join(", ", seen));
        Assert.Contains(MovementStateNames.s_FeetLedge, seen);
        Assert.DoesNotContain(MovementStateNames.s_WaistLedge, seen);
        Assert.DoesNotContain(MovementStateNames.s_BarrierJump, seen);
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.Equal(rig.Pawn.AWaitSt2, rig.Pawn.PawnAnim[0].Sequence);
        Assert.True(rig.Pawn.PawnAnim[0].bLoop);
        Assert.True(rig.Pawn.PawnAnim[0].bPlaying);
        Assert.Equal(0, rig.Pawn.NetworkRootMotion.RootLockType);
        Assert.InRange(rig.Pawn.Location.Z, height + 87f, height + 90f);
        FVector landed = rig.Pawn.Location;
        for (int tick = 0; tick < hz / 2; tick++)
            rig.Step(new PlayerInputFrame { BT_Up = 1 }, 1f / hz);
        Assert.True(rig.Pawn.Location.Y > landed.Y + 50f, $"The merc did not move after mantling: {rig.Pawn.Location}");
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(240)]
    public void AquaExteriorFeetMantleReturnsTheMercToArmedMovement(int hz)
    {
        string folder = Path.Combine(RepoRoot, ".content", "svmap", "AquaE");
        Assert.SkipWhen(!Directory.Exists(folder), "AquaE has not been converted into .content/svmap.");
        PawnRig rig = PawnRig.ForMap(PawnTeam.Merc, folder);
        rig.Place(V(5203.1973f, 3860f, 633.005f), new FRotator(0, 16384, 0), new FRotator(0, 16384, 0), false);
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
            if (climbing && state == MovementStateNames.s_PlayerWalkingSt2H)
            {
                completed = true;
                break;
            }
        }

        // The short detect phase may complete inside the same physics tick on the merc's trajectory.
        Assert.Contains(MovementStateNames.s_FeetLedge, seen);
        Assert.True(completed, string.Join(", ", seen));
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
        Assert.Equal(rig.Pawn.AWaitSt2, rig.Pawn.PawnAnim[0].Sequence);
        Assert.True(rig.Pawn.PawnAnim[0].bLoop);
        Assert.True(rig.Pawn.PawnAnim[0].bPlaying);
        Assert.Equal(0, rig.Pawn.NetworkRootMotion.RootLockType);
        Assert.InRange(rig.Pawn.Location.Y, 3968f, 4003f);
        Assert.InRange(rig.Pawn.Location.Z, 695f, 698f);

        FVector landed = rig.Pawn.Location;
        for (int tick = 0; tick < hz / 2; tick++)
            rig.Step(new PlayerInputFrame { BT_Up = 1 }, 1f / hz);

        Assert.True(rig.Pawn.Location.Y > landed.Y + 50f, $"The merc did not move away from {landed}: {rig.Pawn.Location}");
        Assert.True(rig.Pawn.Location.Z < landed.Z - 20f, $"The merc did not fall from the ledge: {rig.Pawn.Location}");
    }
}
