using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Physics.Collision;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>Walking speeds, input mapping and stance (spec/movement/walking.md, input-and-rotation.md).</summary>
public sealed class WalkingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MercCanWalkSlowerAndReturnToRunning(bool crouched)
    {
        PawnRig run = Rig(PawnTeam.Merc, crouched: crouched);
        PawnRig walk = Rig(PawnTeam.Merc, crouched: crouched);
        run.SetFastSpeed(true);
        walk.SetFastSpeed(false);
        run.Hold(Forward, 60);
        walk.Hold(Forward, 60);
        float slow = walk.Sample().Vel.Size2D();
        float fast = run.Sample().Vel.Size2D();
        Assert.True(slow > 0 && slow < fast, $"walking {slow}, running {fast}");
        walk.Controller.ChangeSpeed();
        walk.Hold(Forward, 60);
        Assert.Equal(fast, walk.Sample().Vel.Size2D(), 1);
    }

    [Fact]
    public void SpySpawnsCrouchedUnarmedAndStandsWithTheRig()
    {
        PawnRig rig = PawnRig.Create(PawnTeam.Spy, Floor().Build());
        Assert.True(rig.Pawn.bIsCrouched);
        Assert.Equal(50f, rig.Pawn.CollisionHeight);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingCr0H));
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.Physics);
    }

    [Fact]
    public void MercSpawnsStandingArmed()
    {
        PawnRig rig = PawnRig.Create(PawnTeam.Merc, Floor().Build());
        Assert.False(rig.Pawn.bIsCrouched);
        Assert.Equal(2, rig.Pawn.WeaponLoaded);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingSt2H));
    }

    [Theory]
    [InlineData(PawnTeam.Spy, false, true, 423.5f, 6600f)]
    [InlineData(PawnTeam.Spy, false, false, 169.4f, 2640f)]
    [InlineData(PawnTeam.Spy, true, true, 221.375f, 3450f)]
    [InlineData(PawnTeam.Merc, false, true, 346.5f, 5400f)]
    [InlineData(PawnTeam.Merc, true, true, 116.91167f, 1823.418f)]
    public void SteadySpeedsMatchTheDecodedFormula(PawnTeam team, bool crouched, bool fast, float speed, float stick)
    {
        PawnRig rig = Rig(team, crouched: crouched);
        rig.SetFastSpeed(fast);
        rig.Hold(Forward, 60);
        PawnSample s = rig.Sample();
        Assert.Equal(speed, s.Gs, 3);
        Assert.Equal(stick, s.Fwd, 2);
        Assert.Equal(speed, s.Vel.Size2D(), 1);
    }

    [Fact]
    public void ButtonsGiveSixThousandTimesTheSpeedMode()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        rig.Step(Forward);
        Assert.Equal(1f, rig.Controller.fNormePadMove);
        Assert.Equal(SPlayerController.cMoveForward | SPlayerController.cMoveUp, rig.Controller.iMove);
        rig.SetFastSpeed(false);
        rig.Step(Forward);
        Assert.Equal(0.4f, rig.Controller.fNormePadMove, 5);
        Assert.Equal(0, rig.Controller.iMove);
    }

    [Fact]
    public void MarioTurnTruncatesTheStickYaw()
    {
        // Facing +Y with the view: the stick direction reads as yaw 16383, so the pawn turns by 65535 units.
        PawnRig rig = Rig(PawnTeam.Spy);
        rig.Step(Forward);
        Assert.Equal(16384 + 65535, rig.Pawn.Rotation.Yaw);
    }

    [Fact]
    public void ReleasingTheStickStopsAtOnce()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        rig.Hold(Forward, 30);
        rig.Step(default);
        Assert.Equal(0f, rig.Pawn.GroundSpeed);
        Assert.Equal(0f, rig.Pawn.Velocity.Size2D(), 3);
    }

    [Fact]
    public void CrouchToggleGoesThroughTheTransitionAndKeepsTheFeet()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        float feet = rig.Pawn.Location.Z - rig.Pawn.CollisionHeight;
        rig.Step(new PlayerInputFrame { bCrouch = 1 });
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingMario_T_StToCr));
        Assert.True(rig.Pawn.bIsCrouched);
        Assert.Equal(feet, rig.Pawn.Location.Z - rig.Pawn.CollisionHeight, 1);
        rig.Hold(default, 60);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingCr0H));
        Assert.Equal(40f, rig.Pawn.BaseEyeHeight);
    }

    [Fact]
    public void MercCrouchUsesItsOwnHeightAndEye()
    {
        PawnRig rig = Rig(PawnTeam.Merc);
        rig.Step(new PlayerInputFrame { bCrouch = 1 });
        rig.Hold(default, 60);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingCr2H));
        Assert.Equal(68f, rig.Pawn.CollisionHeight);
        Assert.Equal(20f, rig.Pawn.BaseEyeHeight);
    }

    [Fact]
    public void StandingUpUnderALowCeilingIsRefusedAndTheLatchReset()
    {
        // A slab 110 above the floor: room to crouch (100 high), not to stand.
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-300f, -300f, 110f), V(300f, 300f, 160f));
        PawnRig rig = Rig(PawnTeam.Spy, b, crouched: true);
        rig.Hold(default, 40);
        rig.Step(new PlayerInputFrame { bCrouch = 1 });
        rig.Hold(default, 30);
        Assert.True(rig.Pawn.bIsCrouched);
        Assert.True(rig.Controller.bCrouchWithRoll);
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingCr0H));
    }

    [Fact]
    public void LowCeilingCapsCrouchedSpeed()
    {
        CollisionDataBuilder b = Floor();
        b.AddBox(V(-300f, -3000f, 110f), V(300f, 3000f, 160f));
        PawnRig rig = Rig(PawnTeam.Spy, b, crouched: true);
        rig.Hold(Forward, 40);
        Assert.Equal(385f * 700f / 3000f, rig.Pawn.GroundSpeed, 2);
    }
}
