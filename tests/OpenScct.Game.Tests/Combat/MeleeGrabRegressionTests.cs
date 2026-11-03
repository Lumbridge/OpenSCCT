using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using OpenScct.Game.Tests.Movement;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Combat;

public sealed class MeleeGrabRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UseBehindMercGrabsFromStandingOrCrouchedOnTheFloor(bool crouched)
    {
        PawnRig rig = Rig(PawnTeam.Spy, crouched: crouched, yaw: 0);
        CombatEquipment.Install(rig.Level);
        RigMate merc = rig.AddMate(PawnTeam.Merc, new FVector(100f, 0f, 0f), 0);
        // WaitStill returns after ten settled frames; the grab also needs the default wake/shock grace to expire.
        rig.Hold(default, 360);
        Assert.Equal(crouched, rig.Pawn.bIsCrouched);
        Assert.True(rig.Pawn.CanUnCrouch());
        Assert.True(rig.Controller.CanGrabPawn(merc.Pawn));

        rig.Step(new PlayerInputFrame { bUsing = 1 });

        Assert.Same(merc.Pawn, rig.Pawn.m_AttackTarget);
        Assert.Same(rig.Pawn, merc.Pawn.m_AttackedBy);
        Assert.StartsWith("s_Grabbing", rig.Controller.GetStateName().ToString());
        Assert.NotEqual(new Name("s_ElbowShooting"), rig.Controller.GetStateName());
        Assert.Equal(new Name("s_GrabbedByPlayer_Begin"), merc.Controller.GetStateName());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8192)]
    public void ElbowVictimKeepsTurnedFacingOnFirstWalkingTickAndFurtherInput(int yaw)
    {
        PawnRig rig = Rig(PawnTeam.Merc, yaw: yaw, options: MovesetTestKit.ContentOptions());
        FVector behind = new FRotator(0, yaw, 0).Vector() * -100f;
        RigMate spy = rig.AddMate(PawnTeam.Spy, behind, yaw);
        rig.WaitStill(240);
        spy.Controller.AskServerToElbowAttack(rig.Pawn, 0, spy.Pawn, default,
            rig.Pawn.Location - spy.Pawn.Location, SDamageType.SDamage_ElbowShock);
        Assert.Equal(new Name("s_ElbowShot_GoBackward"), rig.Controller.GetStateName());
        rig.Step(default);
        int turned = rig.Pawn.Rotation.Yaw;
        Assert.InRange((turned - yaw) & 65535, 32760, 32776);

        for (int i = 0; i < 300 && rig.Controller.IsInState("s_ElbowShot_GoBackward"); i++) rig.Step(default);
        Assert.Equal(new Name("s_PlayerWalkingSt2H"), rig.Controller.GetStateName());
        Assert.Equal(0, (rig.Controller.Rotation.Yaw - turned) & 65535);

        rig.Step(default);
        Assert.Equal(0, (rig.Controller.Rotation.Yaw - turned) & 65535);
        Assert.Equal(0, (rig.Controller.LocalMyDesiredRotation.Yaw - turned) & 65535);
        Assert.Equal(0, (rig.Controller.MyDesiredRotation.Yaw - turned) & 65535);
        rig.Hold(default, 30);
        Assert.Equal(0, (rig.Pawn.Rotation.Yaw - turned) & 65535);
        rig.Step(new PlayerInputFrame { MouseX = 5f });
        Assert.NotEqual(0, (rig.Controller.Rotation.Yaw - turned) & 65535);
    }
}
