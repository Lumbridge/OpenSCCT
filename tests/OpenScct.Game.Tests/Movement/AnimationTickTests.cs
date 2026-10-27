using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Movement;

/// <summary>
/// The animation update as the original runs it (spec/native/ANSWERS-fidelity.md, F3, F4 and F10): the channel
/// rules of USkeletalMeshInstance::PlayAnim and UpdateAnimation, the pawn's update between its controller's
/// PlayerMove and its physics, and the AnimateWalking tween.
/// </summary>
public sealed class AnimationTickTests
{
    private static readonly PawnClip OneShot = new(new Name("TestOnce"), 9, 15f);
    private static readonly PawnClip Looping = new(new Name("TestLoop"), 8, 8f);
    private const float Dt = 0.016635f;

    // ---- channel rules ----------------------------------------------------------------------------------------

    [Fact]
    public void AOneShotWithoutTweenSitsAtZeroUntilItsFirstUpdateOnlyNudgesIt()
    {
        var anim = new PawnAnimation();
        anim.Play(0, OneShot, 1f, 0f, loop: false);
        Assert.Equal(0f, anim[0].Frame);

        anim.Advance(Dt, _ => { });
        Assert.Equal(PawnAnimation.StartFrame, anim[0].Frame);

        anim.Advance(Dt, _ => { });
        Assert.Equal(PawnAnimation.StartFrame + Dt * 15f / 9f, anim[0].Frame, 6);
    }

    [Fact]
    public void ALoopWithoutTweenStartsOffZeroAndPlaysOnItsFirstUpdate()
    {
        var anim = new PawnAnimation();
        anim.Play(0, Looping, 1f, 0f, loop: true);
        Assert.Equal(PawnAnimation.StartFrame, anim[0].Frame);
        anim.Advance(Dt, _ => { });
        Assert.Equal(PawnAnimation.StartFrame + Dt, anim[0].Frame, 6);
    }

    [Fact]
    public void AnUpdateUsesThisTicksTime()
    {
        var anim = new PawnAnimation();
        anim.Play(0, Looping, 1f, 0f, loop: true);
        anim.Advance(0.02f, _ => { });
        Assert.Equal(PawnAnimation.StartFrame + 0.02f, anim[0].Frame, 6);
        anim.Advance(0.01f, _ => { });
        Assert.Equal(PawnAnimation.StartFrame + 0.03f, anim[0].Frame, 6);
    }

    [Fact]
    public void InRootMotionAClipHoldsItsLastFrameForATickBeforeItEnds()
    {
        var anim = new PawnAnimation();
        var ends = new List<int>();
        bool rootMotion = true;
        anim.Play(0, OneShot, 1f, 0f, loop: false);
        int ticks = 0;
        while (anim[0].Frame < anim[0].EndFrame && ticks < 100)
        {
            anim.Advance(Dt, ends.Add, () => rootMotion);
            ticks++;
        }

        // The tick that crosses the last frame clamps there and raises nothing.
        Assert.Equal(anim[0].EndFrame, anim[0].Frame);
        Assert.Empty(ends);
        Assert.True(anim.IsAnimating(0));

        anim.Advance(Dt, ends.Add, () => rootMotion);
        Assert.Equal([0], ends);
        Assert.False(anim.IsAnimating(0));

        // Outside root motion the same clip ends on the tick it gets there.
        var other = new PawnAnimation();
        var otherEnds = new List<int>();
        other.Play(0, OneShot, 1f, 0f, loop: false);
        for (int i = 0; i < ticks; i++)
        {
            other.Advance(Dt, otherEnds.Add, () => false);
        }

        Assert.Equal([0], otherEnds);
    }

    // ---- the pawn's update in the tick --------------------------------------------------------------------------

    [Fact]
    public void AClipTheControllerStartsGetsItsFirstUpdateInTheSameTick()
    {
        // Jump: PlayerMove plays the jump clip without tween; the pawn's update then nudges it to 1e-4.
        PawnRig rig = Rig(PawnTeam.Spy);
        rig.Step(new PlayerInputFrame { Jump = true });
        Assert.Equal(EPhysics.PHYS_Falling, rig.Pawn.Physics);
        Assert.Equal(rig.Pawn.AJumpStart, rig.Pawn.PawnAnim[0].Sequence);
        Assert.Equal(PawnAnimation.StartFrame, rig.Pawn.PawnAnim[0].Frame);
    }

    [Fact]
    public void TheRollEndsAfterThatTicksMoveAndItsStandUpClipWaitsATick()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        rig.Hold(Forward, 40);
        rig.Step(new PlayerInputFrame { BT_Up = 1, bRoll = 1 });
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerRollingUp));
        float rollSpeed = 0f;
        int frames = 0;
        while (rig.Controller.IsInState(MovementStateNames.s_PlayerRollingUp) && frames < 200)
        {
            rig.Step(Forward);
            rollSpeed = rig.Pawn.Velocity.Size2D();
            frames++;
        }

        // The frame the roll clip ends (in the pawn's update, after PlayerMove) still moves at the roll's speed; the
        // stand-up's own PlayerMove runs from the next frame.
        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingMario_T_CrToSt));
        Assert.True(rollSpeed > 600f, $"speed {rollSpeed}");
        Assert.Equal(rig.Pawn.AWaitCrOut0, rig.Pawn.PawnAnim[0].Sequence);
        Assert.Equal(0f, rig.Pawn.PawnAnim[0].Frame);

        rig.Step(Forward);
        Assert.Equal(423.5f, rig.Pawn.Velocity.Size2D(), 1);
        Assert.Equal(PawnAnimation.StartFrame, rig.Pawn.PawnAnim[0].Frame);
        rig.Step(Forward);
        Assert.True(rig.Pawn.PawnAnim[0].Frame > 0.02f);
    }

    [Fact]
    public void OldPhysicsIsTheModeTheLastTickEndedIn()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        Assert.Equal(EPhysics.PHYS_Walking, rig.Pawn.OldPhysics);
        rig.Step(new PlayerInputFrame { Jump = true });
        Assert.Equal(EPhysics.PHYS_Falling, rig.Pawn.OldPhysics);
    }

    // ---- AnimateWalking's tween ------------------------------------------------------------------------------

    [Theory]
    [InlineData(EPhysics.PHYS_Walking, (byte)0, PawnActions.StandTween)]
    [InlineData(EPhysics.PHYS_RootMotion, (byte)0, 0f)]
    [InlineData(EPhysics.PHYS_Falling, (byte)0, 0f)]
    [InlineData(EPhysics.PHYS_Walking, (byte)1, PawnActions.LeaveWallTween)]
    [InlineData(EPhysics.PHYS_RootMotion, (byte)1, PawnActions.LeaveWallTween)]
    public void TheRestingLoopTweensByHowThePawnArrived(EPhysics oldPhysics, byte oldBtw, float tween)
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SBasePawn pawn = rig.Pawn;
        pawn.OldPhysics = oldPhysics;
        pawn.OldBTW = (BTW_)oldBtw;
        pawn.LastZipP = V(1f, 2f, 3f);

        // Another clip first: replaying the loop channel 0 already plays only changes its rate (PlayAnim's same-loop rule).
        pawn.PawnAnim.Play(0, PawnActions.Clip(pawn, pawn.ALandLow), 1f, 0f, loop: false);
        PawnActions.AnimateWalking(pawn, pawn.AWaitSt0);

        PawnAnimChannel c = pawn.PawnAnim[0];
        Assert.Equal(pawn.AWaitSt0, c.Sequence);
        if (tween == 0f)
        {
            Assert.Equal(PawnAnimation.StartFrame, c.Frame);
        }
        else
        {
            Assert.Equal(-1f / c.Frames, c.Frame, 6);
            Assert.Equal(1f / (tween * c.Frames), c.TweenRate, 4);
        }

        Assert.Equal(default, pawn.OldBTW);
        Assert.Equal(MovementK.FarZipPoint, pawn.LastZipP);
        Assert.Equal(EPhysics.PHYS_Walking, pawn.Physics);
    }

    [Fact]
    public void AForwardJumpOnChannelZeroShortensTheTween()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SBasePawn pawn = rig.Pawn;
        pawn.PawnAnim.Play(0, PawnActions.Clip(pawn, pawn.AJumpForwardL), 1f, 0f, loop: false);
        pawn.OldPhysics = EPhysics.PHYS_Walking;
        PawnActions.AnimateWalking(pawn, pawn.AWaitSt0);
        Assert.Equal(PawnActions.JumpForwardTween, pawn.BlendChangeTime);
        Assert.Equal(1f / (PawnActions.JumpForwardTween * pawn.PawnAnim[0].Frames), pawn.PawnAnim[0].TweenRate, 4);
    }

    [Fact]
    public void StandingUpPlaysItsClipWithoutTween()
    {
        PawnRig rig = Rig(PawnTeam.Spy, crouched: true);
        rig.Step(new PlayerInputFrame { bCrouch = 1 });
        int guard = 0;
        while (!rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingMario_T_CrToSt) && guard++ < 10)
        {
            rig.Step(default);
        }

        Assert.True(rig.Controller.IsInState(MovementStateNames.s_PlayerWalkingMario_T_CrToSt));
        Assert.Equal(rig.Pawn.AWaitCrOut0, rig.Pawn.PawnAnim[0].Sequence);
        Assert.True(rig.Pawn.PawnAnim[0].Frame >= 0f);
    }
}
