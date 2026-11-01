using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Camera;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Camera;

public sealed class FollowingResumeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PausingOrReadingAnotherViewPreservesInProgressFollowingSmoothing(bool pause)
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SPlayerController pc = rig.Controller;
        SPlayerCam camera = PlayerCamera.Of(pc)!;
        PlayerCalcView.FrameView(pc, out _, out _, out _);
        camera.ActivateSmooth(MovementK.HohSmooth, SmoothIsAlwaysActive: true);
        rig.Pawn.Location += V(1000, 0, 0);
        PlayerCalcView.FrameView(pc, out _, out _, out _);
        FVector before = camera.m_prevStartPos;

        if (pause)
        {
            rig.Level.Info.Pauser = new PlayerReplicationInfo();
            PlayerCalcView.FrameView(pc, out _, out _, out _);
            rig.Level.Info.Pauser = null;
        }
        else
        {
            pc.FreeCameraFlying = 1;
            PlayerCalcView.NativePlayerCalcView(pc, false, out _, out _, out _);
            pc.FreeCameraFlying = 0;
        }

        PlayerCalcView.FrameView(pc, out _, out _, out _);
        Assert.InRange(FVector.Dist(before, camera.m_prevStartPos), 0f, 1f);
        Assert.True(FVector.Dist(camera.m_prevStartPos, rig.Pawn.Location) > 900f);
    }

    [Theory]
    [InlineData("binoculars")]
    [InlineData("attached")]
    [InlineData("free")]
    [InlineData("fixed")]
    [InlineData("intro")]
    [InlineData("s_UseObject_StickyCam")]
    [InlineData("s_UseObject_CamNetwork")]
    [InlineData("s_UseObject_Mirador")]
    public void ReturningFromAnotherViewReanchorsFollowingAtTheCurrentPawn(string view)
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SPlayerController pc = rig.Controller;
        SPlayerCam camera = PlayerCamera.Of(pc)!;
        pc.bBehindView = true;
        pc.ViewTarget = rig.Pawn;
        PlayerCalcView.FrameView(pc, out _, out _, out _);
        Name walking = pc.GetStateName();

        switch (view)
        {
            case "binoculars":
                pc.GotoState("s_Binoculars_Att");
                pc.bBinocularVision = true;
                break;
            case "attached":
                camera.AttachCamToBone(true, "B Head");
                pc.bFirstPersonWithPawn = true;
                break;
            case "free":
                pc.FreeCameraFlying = 1;
                break;
            case "fixed":
                camera.GotoState("s_Fixed");
                break;
            case "intro":
                camera.GotoState("s_IntroCinematic");
                break;
            default:
                rig.Pawn.UsedObject = rig.Level.SpawnActor(new SStickyCam(), V(0, 0, 200), default);
                pc.GotoState(view);
                break;
        }

        // A mover, knockback or attached animation can displace the pawn while another view is active.
        rig.Pawn.Location += V(4000, 0, 0);
        PlayerCalcView.FrameView(pc, out _, out _, out _);
        pc.FreeCameraFlying = 0;
        camera.AttachCamToBone(false);
        camera.GotoState("s_Following");
        pc.GotoState(walking);
        pc.bFirstPersonWithPawn = false;
        pc.bBehindView = true;
        pc.ViewTarget = rig.Pawn;
        // Traversal transitions can enable slow smoothing as the normal view returns.
        camera.ActivateSmooth(MovementK.HohSmooth);
        PlayerCalcView.FrameView(pc, out _, out FVector returned, out _);

        Assert.InRange(FVector.Dist(returned, rig.Pawn.Location), 100f, 400f);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangingFollowedPawnReanchorsWithoutWaitingForSmoothing(bool spectating)
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SPlayerController pc = rig.Controller;
        SPlayerCam camera = PlayerCamera.Of(pc)!;
        pc.bBehindView = true;
        pc.ViewTarget = rig.Pawn;
        PlayerCalcView.FrameView(pc, out _, out _, out _);
        SBasePawn next = rig.Level.SpawnActor(new SPawnAttaque_1Mesh(), rig.Pawn.Location + V(4000, 0, 0), rig.Pawn.Rotation)!;
        if (spectating)
        {
            pc.GotoState("s_MultiSpectating");
            pc.Pawn = null;
            pc.SBPawn = null;
            pc.SpecTarget = next;
        }
        else
        {
            pc.Pawn = next;
            pc.SBPawn = next;
        }

        pc.ViewTarget = next;
        camera.ActivateSmooth(MovementK.HohSmooth);
        // Rotation updates also assign m_sam, so it cannot alone identify the previous solved pawn.
        camera.FollowingRotation(next);
        PlayerCalcView.FrameView(pc, out _, out FVector returned, out _);

        Assert.InRange(FVector.Dist(returned, next.Location), 100f, 400f);
    }
}
