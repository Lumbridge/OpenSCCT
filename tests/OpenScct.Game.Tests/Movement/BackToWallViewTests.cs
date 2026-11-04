using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

public sealed class BackToWallViewTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void HoldingTheWallLimitsTheViewWithoutTurningThePawn(int direction)
    {
        var level = Floor();
        level.AddBox(V(-200f, -100f, 0f), V(200f, -40f, 300f));
        PawnRig rig = Rig(PawnTeam.Spy, level);
        rig.Step(new PlayerInputFrame { bUseBackToWall = 1 });
        for (int frame = 0; frame < 120 && !rig.Controller.IsInState(MovementStateNames.s_BTW_up); frame++)
        {
            rig.Step(default);
        }

        Assert.True(rig.Controller.IsInState(MovementStateNames.s_BTW_up));
        int pawnYaw = rig.Pawn.Rotation.Yaw;
        for (int frame = 0; frame < 120; frame++)
        {
            rig.Step(new PlayerInputFrame { MouseX = direction * 8000f });
            Assert.InRange(FRotator.NormalizeAxis(rig.Controller.Rotation.Yaw - pawnYaw), -20000, 20000);
        }

        Assert.Equal(direction * 20000, FRotator.NormalizeAxis(rig.Controller.Rotation.Yaw - pawnYaw));
        Assert.Equal(pawnYaw, rig.Pawn.Rotation.Yaw);
    }

    [Theory]
    [InlineData("s_BTWFireStandLeft", -10000, 50)]
    [InlineData("s_BTWFireStandRight", -50, 10000)]
    [InlineData("s_BTWFireCrouchLeft", -10000, 50)]
    [InlineData("s_BTWFireCrouchRight", -50, 10000)]
    public void CornerFireLimitsAndPredictionRemainStableAcrossWorldYawZero(string state, int minYaw, int maxYaw)
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SPlayerController pc = rig.Controller;
        rig.Pawn.Rotation = new FRotator(0, 32766, 0);
        pc.GotoState(new Name(state));
        Assert.True(pc.TryGetBtwViewLimits(out CameraWindow window, out int reference));
        Assert.Equal(minYaw, window.MinYaw);
        Assert.Equal(maxYaw, window.MaxYaw);
        pc.Rotation = new FRotator(0, reference, 0);
        pc.fSensibiliteSouris = 1f;
        for (int frame = 0; frame < 20; frame++)
        {
            float turn = frame % 2 == 0 ? 100f : -100f;
            FRotator before = pc.Rotation;
            FRotator predicted = pc.CalculateLimitedRotation(Sim.DeltaTime, turn, 0f,
                window.MinYaw, window.MaxYaw, window.MinPitch, window.MaxPitch, reference, 0);
            Assert.Equal(before, pc.Rotation);
            pc.fTurnValue = turn;
            pc.UpdateLimitedRotation(Sim.DeltaTime, window.MinYaw, window.MaxYaw, window.MinPitch, window.MaxPitch, reference, 0);
            Assert.Equal(predicted, pc.Rotation);
            Assert.InRange(FRotator.NormalizeAxis(pc.Rotation.Yaw - before.Yaw), -84, 84);
            Assert.InRange(FRotator.NormalizeAxis(pc.Rotation.Yaw - reference), minYaw, maxYaw);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void ALongMouseTurnStopsAtItsOwnLimitInsteadOfWrappingToTheOtherSide(int direction)
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SPlayerController pc = rig.Controller;
        const int reference = 65000;
        pc.Rotation = new FRotator(0, reference, 0);
        pc.fSensibiliteSouris = 1f;
        pc.fTurnValue = direction * 1000f;
        pc.UpdateLimitedRotation(1f, -10000, 50, -10000, 10000, reference, 0);
        Assert.Equal(direction > 0 ? 50 : -10000, FRotator.NormalizeAxis(pc.Rotation.Yaw - reference));
    }
}
