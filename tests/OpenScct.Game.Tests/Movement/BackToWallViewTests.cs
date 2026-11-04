using OpenScct.Core;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

public sealed class BackToWallViewTests
{
    private static PawnRig OnWall(bool wallInFront)
    {
        var level = Floor();
        level.AddBox(V(-200f, -100f, 0f), V(200f, -40f, 300f));
        PawnRig rig = Rig(PawnTeam.Spy, level, yaw: wallInFront ? 49152 : 16384);
        rig.Step(new PlayerInputFrame { bUseBackToWall = 1 });
        for (int frame = 0; frame < 120 && !rig.Controller.IsInState(MovementStateNames.s_BTW_up); frame++)
        {
            rig.Step(default);
        }

        Assert.True(rig.Controller.IsInState(MovementStateNames.s_BTW_up));
        return rig;
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    [InlineData(-1, true)]
    [InlineData(1, true)]
    public void HoldingTheWallLimitsTheViewWithoutTurningThePawn(int direction, bool wallInFront)
    {
        PawnRig rig = OnWall(wallInFront);
        Assert.True(rig.Controller.TryGetBtwViewLimits(out _, out int reference));
        Assert.Equal(wallInFront ? 49152 : 16384, reference & 65535);
        int pawnYaw = rig.Pawn.Rotation.Yaw;
        for (int frame = 0; frame < 120; frame++)
        {
            rig.Step(new PlayerInputFrame { MouseX = direction * 8000f });
            Assert.InRange(FRotator.NormalizeAxis(rig.Controller.Rotation.Yaw - reference), -20000, 20000);
        }

        Assert.Equal(direction * 20000, FRotator.NormalizeAxis(rig.Controller.Rotation.Yaw - reference));
        Assert.Equal(pawnYaw, rig.Pawn.Rotation.Yaw);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EitherWallEntryKeepsTheViewAndCameraRelativeSidestep(bool wallInFront)
    {
        PawnRig rig = OnWall(wallInFront);
        int entryYaw = wallInFront ? 49152 : 16384;
        rig.Hold(default, 60);
        Assert.Equal(entryYaw, rig.Controller.Rotation.Yaw & 65535);
        float startX = rig.Pawn.Location.X;
        rig.Hold(new PlayerInputFrame { BT_Left = 1 }, 10);
        Assert.Equal(130.9f, rig.Pawn.GroundSpeed, 1);
        Assert.Equal(130.9f, rig.Pawn.Velocity.Size2D(), 1);
        Assert.True(wallInFront ? rig.Pawn.Location.X < startX : rig.Pawn.Location.X > startX);

        // Release a real peek pose while looking inward. Its return must not pull that view to a sideways
        // limit, where the native camera-relative input no longer produces a sidestep bit.
        rig.Controller.GotoState(MovementStateNames.s_BTW_StandLeft);
        rig.Controller.Rotation = new FRotator(0, rig.Pawn.Rotation.Yaw + 32768, 0);
        rig.Step(default);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_BTW_up));
        Assert.True(rig.Controller.TryGetBtwViewLimits(out _, out int reference));
        int inwardYaw = (rig.Pawn.Rotation.Yaw + 32768) & 65535;
        Assert.Equal(inwardYaw, reference & 65535);
        rig.Hold(default, 60);
        Assert.Equal(inwardYaw, rig.Controller.Rotation.Yaw & 65535);
        startX = rig.Pawn.Location.X;
        rig.Hold(new PlayerInputFrame { BT_Left = 1 }, 10);
        Assert.Equal(130.9f, rig.Pawn.GroundSpeed, 1);
        Assert.True(rig.Pawn.Location.X < startX);

        rig.Step(new PlayerInputFrame { bCrouch = 1 });
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_BTW_down));
        Assert.True(rig.Controller.TryGetBtwViewLimits(out _, out reference));
        Assert.Equal(inwardYaw, reference & 65535);

        rig.Controller.Rotation = new FRotator(0, rig.Pawn.Rotation.Yaw, 0);
        rig.Controller.GotoState(MovementStateNames.s_EnterBTW);
        Assert.Equal(0, rig.Controller.BtwViewYawOffset);
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
