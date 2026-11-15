using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Game.Svm;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class RifleAimMovementTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AimingQuartersWalkingAndRunningThenRestoresSpeed(bool crouched, bool fast)
    {
        var rig = Rig(PawnTeam.Merc, crouched: crouched);
        var rifle = rig.Pawn.XLevel.SpawnActor(new AK47(), rig.Pawn.Location, default)!;
        rifle.GiveTo(rig.Pawn);
        rig.Pawn.WeaponLoaded = 2;
        rifle.GotoState("s_Selected");
        rig.SetFastSpeed(fast);
        rig.Hold(Forward, 60);
        float hip = rig.Sample().Vel.Size2D();
        Assert.True(hip > 0);

        rifle.SetAiming(rig.Controller, true);
        Assert.True(rifle.AimRequested);
        rig.Hold(Forward, 60);
        Assert.InRange(System.MathF.Abs(hip * .25f - rig.Sample().Vel.Size2D()), 0, .01f);

        rifle.SetAiming(rig.Controller, false);
        rig.Hold(Forward, 60);
        Assert.InRange(System.MathF.Abs(hip - rig.Sample().Vel.Size2D()), 0, .01f);
    }

    [Fact]
    public void AimingDoesNotMoveTheAnalogDeadZoneOrStopAnAcceptedSlowInput()
    {
        var pawn = new SPawnDefense_1Mesh();
        pawn.SetPhysics(EPhysics.PHYS_Walking);
        var rifle = new AK47(); pawn.MyWeapon = rifle;
        var normal = new OpenScct.Core.FVector(700, 0, 0);
        SPlayerController.WeaponGroundSpeed(pawn, ref normal);
        float hip = pawn.GroundSpeed;
        Assert.True(hip > 0);
        rifle.AimRequested = true;
        var aiming = new OpenScct.Core.FVector(700, 0, 0);
        SPlayerController.WeaponGroundSpeed(pawn, ref aiming);
        Assert.Equal(hip * .25f, pawn.GroundSpeed, 5);
        Assert.NotEqual(default, aiming);
    }
}
