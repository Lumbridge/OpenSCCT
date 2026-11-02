using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

public sealed class TraversalPoseResetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturningToWalkingDuringHolsterAllowsTheNextPipeGrab(bool crouched)
    {
        PawnRig rig = Rig(PawnTeam.Spy, crouched: crouched);
        SBasePawn pawn = rig.Pawn;
        pawn.ExecuteAction(crouched ? SBasePawn.SA_WALK_CR_2Hto0H : SBasePawn.SA_WALK_ST_2Hto0H);
        Assert.True(pawn.bWeaponTransit);
        Assert.True(pawn.PawnAnim[SBasePawn.DRAWCHANNEL].bPlaying);

        rig.Controller.GotoWaitingState();
        for (int tick = 0; tick < 120; tick++) pawn.AdvancePredictionAnimation(1f / 60f);

        Assert.False(pawn.bWeaponTransit);
        Assert.Equal(0f, pawn.PawnAnim[SBasePawn.DRAWCHANNEL].BlendAlpha);
        rig.Controller.eventPipe(default, default);
        Assert.Equal(MovementStateNames.s_GrabPipe, rig.Controller.GetStateName());
        Assert.Equal(EPhysics.PHYS_OnPipe, pawn.Physics);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DroppingFromTheBarClearsItsDrawAndAimState(bool drawing)
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SBasePawn pawn = rig.Pawn;
        pawn.Location = new FVector(0f, 0f, 600f);
        pawn.ExecuteAction(SBasePawn.SA_PIPH_GRAB);
        pawn.ExecuteAction(drawing ? SBasePawn.SA_PIPH_TR_FIRE2 : SBasePawn.SA_PIPH_FIRE2);
        rig.Controller.GotoState(drawing ? MovementStateNames.s_PipH_TR2Fire2 : MovementStateNames.s_PipH_Fire2);
        rig.Controller.bCrouchWithRoll = true;

        rig.Controller.eventPlayerMove(1f / 60f);

        Assert.Equal(MovementStateNames.s_PlayerJump, rig.Controller.GetStateName());
        Assert.Equal(EPhysics.PHYS_Falling, pawn.Physics);
        Assert.False(pawn.bWeaponTransit);
        Assert.Equal(PipHFire_.NOPipH, pawn.PipHFire);
        Assert.Equal(IK_Type_.IKT_WEAPONLOADED, pawn.IK_Type);
        Assert.Equal(pawn.AFall, pawn.PawnAnim[0].Sequence);
        Assert.True(pawn.PawnAnim[0].bLoop);
    }
}
