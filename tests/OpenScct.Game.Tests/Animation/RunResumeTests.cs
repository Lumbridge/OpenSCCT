using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Animation;

public sealed class RunResumeTests
{
    [Theory]
    [InlineData(ENetRole.ROLE_Authority, false)]
    [InlineData(ENetRole.ROLE_Authority, true)]
    [InlineData(ENetRole.ROLE_AutonomousProxy, false)]
    [InlineData(ENetRole.ROLE_AutonomousProxy, true)]
    [InlineData(ENetRole.ROLE_SimulatedProxy, false)]
    [InlineData(ENetRole.ROLE_SimulatedProxy, true)]
    public void RunningReplacesAnInterruptedOrFinishedRootMotionPose(ENetRole role, bool completed)
    {
        PawnRig rig = Rig(PawnTeam.Merc);
        SBasePawn pawn = rig.Pawn;
        pawn.Role = role;
        pawn.ExecuteAction(SBasePawn.SA_BERSERK_ATTACK);
        pawn.NetworkRootMotion.LockRootMotion(pawn.iLockRootMotionType);
        pawn.OldPhysics = EPhysics.PHYS_RootMotion;
        if (completed)
        {
            // A proxy can receive the run action after its prior body clip has already ended.
            for (int tick = 0; tick < 180; tick++) pawn.AdvancePredictionAnimation(1f / 60f);
            Assert.False(pawn.PawnAnim[0].bPlaying);
        }

        pawn.ExecuteAction(SBasePawn.SA_RUN_ST_2H);

        Assert.Equal(EPhysics.PHYS_Walking, pawn.Physics);
        Assert.Equal(0, pawn.NetworkRootMotion.RootLockType);
        Assert.Equal(pawn.AWaitSt2, pawn.PawnAnim[0].Sequence);
        Assert.True(pawn.PawnAnim[0].bLoop);
        Assert.True(pawn.PawnAnim[0].bPlaying);
        Assert.Equal(new Name("JoggStAlFd2"), pawn.ForwardAnim);
        Assert.Equal(MovementK.MercRunBlendCoeff, pawn.fBlendCoeffWalkAnim);
    }
}
