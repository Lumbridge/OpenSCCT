using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.Movement;
using OpenScct.Game.SBase;
using Xunit;
using static OpenScct.Game.Tests.Movement.MovementTestKit;

namespace OpenScct.Game.Tests.Animation;

/// <summary>
/// The walk blender (spec/native/ANSWERS-anim.md A6) and the animation side of the actions (ActionAnimation) on a
/// rig: the walk set on channels 4 to 8, the step size, the slow walk and run blends, the turn in place, the roll
/// blend into channel 0 and the back-to-wall set.
/// </summary>
public sealed class WalkBlenderTests
{
    [Fact]
    public void TheFirstUpdateFillsTheWalkChannelsWithWaitLoops()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        PawnAnimation a = rig.Pawn.PawnAnim;
        Assert.True(rig.Pawn.bInitializeAnimation);
        for (int ch = 2; ch < 14; ch++)
        {
            Assert.False(a[ch].Sequence.IsNone, $"channel {ch}");
            Assert.True(a[ch].bLoop, $"channel {ch}");
            Assert.Equal(ch != 5, a[ch].bNotifyIgnore);
        }
    }

    [Fact]
    public void StandingSetsTheUnarmedWalkSet()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SBasePawn p = rig.Pawn;
        Assert.Equal(p.AWalkStFd0, p.ForwardAnim);
        Assert.Equal(p.ARunStFd0, p.RunAnim);
        Assert.Equal(p.AWalkStRt0, p.RightAnim);
        Assert.Equal(p.LowLimitForWalkOnly, p.fWalkSpeedLimit);
        Assert.Equal(p.HighLimitForRunOnly, p.fRunSpeedLimit);
        Assert.Equal(120.6f, p.fBlendCoeffWalkAnim);
    }

    [Fact]
    public void WalkingStepsTheForwardChannelInAndKeepsTheOthersInPhase()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        PawnAnimation a = rig.Pawn.PawnAnim;
        rig.Step(Forward);
        float step = (1f / 60f) / rig.Pawn.BlendChangeTime;
        Assert.Equal(rig.Pawn.AWalkStFd0, a[5].Sequence);
        Assert.Equal(step, a[5].BlendAlpha, 4);
        rig.Hold(Forward, 30);
        Assert.Equal(a[5].Frame, a[4].Frame);
        Assert.Equal(a[5].Frame, a[6].Frame);
        Assert.Equal(0f, a[4].Rate);

        // At full speed the run channel carries the cycle and the walk channel fades.
        Assert.True(a[4].BlendAlpha > 0.9f, $"run alpha {a[4].BlendAlpha}");
        Assert.True(a[5].BlendAlpha < 0.1f, $"walk alpha {a[5].BlendAlpha}");
    }

    [Fact]
    public void StandingStillFadesTheWalkChannels()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        rig.Hold(Forward, 40);
        rig.Hold(default, 60);
        PawnAnimation a = rig.Pawn.PawnAnim;
        for (int ch = 4; ch < 14; ch++)
        {
            Assert.Equal(0f, a[ch].BlendAlpha);
        }
    }

    [Fact]
    public void ATurnFasterThan5000PerSecondTurnsInPlace()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SBasePawn p = rig.Pawn;
        p.Rotation = new FRotator(0, p.Rotation.Yaw - 200, 0);
        PawnMovementAnimation.Update(p, 1f / 60f);
        PawnAnimation a = p.PawnAnim;
        Assert.Equal(p.TurnAnim, a[2].Sequence);
        Assert.Equal((1f / 60f) * AnimationK.TurnStepPerSecond, a[2].BlendAlpha, 4);
        Assert.True(a[2].Rate > 0f);
        Assert.Equal(p.Rotation.Yaw, (int)p.OldRotYaw);
    }

    [Fact]
    public void TheRollPlaysOnChannel15AndBlendsIntoChannelZero()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        rig.Hold(Forward, 40);
        rig.Step(new PlayerInputFrame { BT_Up = 1, bRoll = 1 });
        SBasePawn p = rig.Pawn;
        Assert.True(p.bIsRolling);
        Assert.Equal(SBasePawn.DBLJUMPCHANNEL, p.ChannelForBlendWithC0);
        Assert.Equal(ActionAnimation.RollBlendTime, p.fBlendTimeWithC0);
        string clip = p.PawnAnim[SBasePawn.DBLJUMPCHANNEL].Sequence.ToString();
        Assert.StartsWith("RollStJpFd", clip, System.StringComparison.OrdinalIgnoreCase);
        Assert.True(p.PawnAnim[SBasePawn.DBLJUMPCHANNEL].BlendAlpha > 0f);
    }

    [Fact]
    public void TheRollPicksTheLeadingFootOfTheRunCycle()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SBasePawn p = rig.Pawn;
        p.PawnAnim.SetFrame(4, 0.3f);
        Assert.True(ActionAnimation.IsLeftFoot(p));
        ActionAnimation.PlayRoll(p, up: false);
        Assert.Equal("RollStAlFdL", p.PawnAnim[SBasePawn.DBLJUMPCHANNEL].Sequence.ToString(), ignoreCase: true);
        p.PawnAnim.SetFrame(4, 0.8f);
        ActionAnimation.PlayRoll(p, up: true);
        Assert.Equal("RollStJpFdR", p.PawnAnim[SBasePawn.DBLJUMPCHANNEL].Sequence.ToString(), ignoreCase: true);
    }

    [Fact]
    public void AnimateBtwSetsTheShimmySetAndTheHoldLoop()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SBasePawn p = rig.Pawn;
        p.BTW = BTW_.BTWStand;
        p.OldBTW = BTW_.NOBTW;
        ActionAnimation.AnimateBTW(p);
        Assert.Equal("BackStAlFdL", p.RightAnim.ToString());
        Assert.Equal("BackStAlFdR", p.LeftAnim.ToString());
        Assert.Equal(ActionAnimation.BtwStandCoeff, p.fBlendCoeffWalkAnim);
        Assert.Equal("BackStAlNt0", p.PawnAnim[0].Sequence.ToString());
        Assert.Equal(0f, p.PawnAnim[0].TweenRate);
        Assert.Equal(BTW_.BTWStand, p.OldBTW);

        p.BTW = BTW_.BTWStandLeft;
        ActionAnimation.AnimateBTW(p);
        Assert.Equal("BackStPkNtL", p.PawnAnim[0].Sequence.ToString());
        Assert.Equal(1f / (p.PawnAnim[0].Frames * ActionAnimation.BtwPoseTween), p.PawnAnim[0].TweenRate, 4);
    }

    [Fact]
    public void EveryActionResetsTheBlendIntoChannelZero()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        SBasePawn p = rig.Pawn;
        p.fBlendTimeWithC0 = 0.1f;
        p.BlendChangeTime = 3f;
        p.ExecuteAction(SBasePawn.SA_WANTS_TO_CROUCH);
        Assert.Equal(0f, p.fBlendTimeWithC0);
        Assert.Equal(ActionAnimation.DefaultBlendChangeTime, p.BlendChangeTime);
    }

    [Fact]
    public void TurnHeadPacksTheViewForTheAimBlends()
    {
        PawnRig rig = Rig(PawnTeam.Spy);
        rig.Controller.Rotation = new FRotator(1000, 20000, 0);
        PawnMovementAnimation.TurnHead(rig.Pawn);
        Assert.Equal((1000 << 16) | 20000, rig.Pawn.iYawPitchFromView);
    }
}
