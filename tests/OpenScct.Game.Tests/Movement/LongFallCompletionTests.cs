using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

public sealed class LongFallCompletionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LeavingAWallJumpUsesThePendingFallVelocityAndCurrentWeaponPose(bool armed, bool hasPendingVelocity)
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SBasePawn pawn = rig.Pawn;
        pawn.WeaponLoaded = armed ? (byte)2 : (byte)0;
        pawn.ExecuteAction(SBasePawn.SA_WLJP_SALTO);
        pawn.NetworkRootMotion.LockRootMotion(pawn.iLockRootMotionType);
        FVector current = new(12f, 34f, -56f);
        FVector pending = hasPendingVelocity ? pawn.vSpeedAfterRM : default;
        pawn.Velocity = current;
        pawn.vSpeedAfterRM = pending;
        pawn.PipHFire = PipHFire_.PipH2;
        pawn.bWeaponTransit = true;

        // ExecuteAction must reach the long-fall case, which restores the velocity queued by the salto.
        pawn.ExecuteAction(SBasePawn.SA_LONG_FALL_ST_0H);

        Assert.Equal(hasPendingVelocity ? pending : current, pawn.Velocity);
        Assert.Equal(EPhysics.PHYS_Falling, pawn.Physics);
        Assert.Equal(0, pawn.NetworkRootMotion.RootLockType);
        Assert.Equal(PipHFire_.NOPipH, pawn.PipHFire);
        Assert.False(pawn.bWeaponTransit);
        PawnAnimChannel fall = pawn.PawnAnim[0];
        Assert.Equal(armed ? pawn.AFall2H : pawn.AFall, fall.Sequence);
        Assert.True(fall.bLoop);
        Assert.True(fall.bPlaying);
        Assert.Equal(0.35f, -fall.Frame / fall.TweenRate, 5);
    }
}
