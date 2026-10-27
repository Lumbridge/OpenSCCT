using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.Movement;
using Xunit;

namespace OpenScct.Game.Tests.Animation;

/// <summary>
/// The channel natives and the per-tick update (spec/native/anim.md, spec/native/ANSWERS-anim.md A1 to A5) on a bare
/// channel set: PlayAnim's tween and loop rules, the passes of UpdateAnimation, AnimEnd for one-shots and loops, timed
/// blends, the walk blender's helpers and the tween cache.
/// </summary>
public sealed class ChannelModelTests
{
    private const float Dt = 1f / 60f;
    private static readonly PawnClip Loop8 = new(new Name("TestLoop8"), 8, 8f);
    private static readonly PawnClip Once10 = new(new Name("TestOnce10"), 10, 15f);
    private static readonly PawnClip Pose1 = new(new Name("TestPose1"), 1, 15f);
    private static readonly PawnClip Other = new(new Name("TestOther"), 12, 15f);

    // ---- PlayAnim ----------------------------------------------------------------------------------------------

    [Fact]
    public void ReplayingTheLoopAChannelAlreadyPlaysOnlyChangesItsRate()
    {
        var a = new PawnAnimation();
        a.PlayAnim(0, Loop8, 1f, 0f, loop: true);
        a.Advance(0.2f, _ => { });
        float frame = a[0].Frame;
        a.PlayAnim(0, Loop8, 2f, 0.25f, loop: true);
        Assert.Equal(frame, a[0].Frame);
        Assert.Equal(2f, a[0].Rate, 6);
        Assert.Equal(2f, a[0].OldAnimRate, 6);
    }

    [Fact]
    public void AOneShotOnAnEmptyChannelPlaysWithoutTween()
    {
        var a = new PawnAnimation();
        a.PlayAnim(3, Once10, 1f, 0.5f, loop: false);
        Assert.Equal(0f, a[3].Frame);
        Assert.Equal(0f, a[3].TweenRate);
    }

    [Fact]
    public void ATweenStartsOneFrameBeforeTheClipAndRunsForTheTweenTime()
    {
        var a = new PawnAnimation();
        a.PlayAnim(0, Other, 1f, 0f, loop: false);
        a.PlayAnim(0, Once10, 1f, 0.2f, loop: false);
        Assert.Equal(-0.1f, a[0].Frame, 6);
        Assert.Equal(1f / (10f * 0.2f), a[0].TweenRate, 6);
        Assert.Equal(1f - 0.1f, a[0].AnimLast, 6);
        Assert.Equal(15f / 10f, a[0].Rate, 6);
    }

    [Fact]
    public void AOneFramePoseTweensAtTenPerSecondWithoutATweenTime()
    {
        var a = new PawnAnimation();
        a.PlayAnim(0, Pose1, 1f, 0f, loop: false);
        Assert.Equal(-1f, a[0].Frame);
        Assert.Equal(10f, a[0].TweenRate);
        Assert.Equal(0f, a[0].Rate);
    }

    [Fact]
    public void TweenAnimIsAPoseAndRaisesAnimEndWhenItsTweenEnds()
    {
        var a = new PawnAnimation();
        a.PlayAnim(0, Other, 1f, 0f, loop: false);
        a.TweenAnim(0, Once10, 0.1f);
        Assert.Equal(0f, a[0].Rate);
        Assert.Equal(0f, a[0].AnimLast);
        var ends = new List<int>();
        for (int i = 0; i < 10 && ends.Count == 0; i++)
        {
            a.Advance(Dt, ends.Add);
        }

        Assert.Equal([0], ends);
        Assert.False(a.IsAnimating(0));
    }

    [Fact]
    public void ANegativeRateOneShotIsRefused()
    {
        var a = new PawnAnimation();
        Assert.False(a.PlayAnim(0, Once10, -1f, 0f, loop: false));
        Assert.True(a[0].Sequence.IsNone);
    }

    [Fact]
    public void TweenMinusOneUsesTheOldRate()
    {
        var a = new PawnAnimation();
        a.PlayAnim(0, Other, 1f, 0f, loop: false);
        float oldRate = a[0].Rate;
        a.PlayAnim(0, Once10, 1f, -1f, loop: false);
        Assert.Equal(oldRate, a[0].TweenRate, 6);
        Assert.Equal(-0.1f, a[0].Frame, 6);
    }

    // ---- UpdateAnimation ---------------------------------------------------------------------------------------

    [Fact]
    public void ALoopRaisesAnimEndWhenItCrossesItsLastFrameAndWrapsToTheStartFrame()
    {
        var a = new PawnAnimation();
        a.PlayAnim(0, Loop8, 1f, 0f, loop: true);
        var ends = new List<int>();
        int crossed = -1;
        for (int i = 0; i < 70; i++)
        {
            float before = a[0].Frame;
            a.Advance(Dt, ends.Add);
            if (crossed < 0 && before < a[0].AnimLast && a[0].Frame >= a[0].AnimLast)
            {
                crossed = i;
            }

            if (crossed >= 0 && ends.Count == 0)
            {
                Assert.Fail("no AnimEnd on the AnimLast crossing");
            }
        }

        Assert.True(crossed >= 0);
        Assert.Single(ends);

        // Past 1 the frame restarts at 1e-4 (the time left is not carried).
        var b = new PawnAnimation();
        b.PlayAnim(0, Loop8, 1f, 0f, loop: true);
        b.SetFrame(0, 0.99f);
        b.Advance(Dt, _ => { });
        Assert.Equal(PawnAnimation.StartFrame, b[0].Frame);
    }

    [Fact]
    public void ANotifyIgnoringChannelRaisesNoAnimEnd()
    {
        var a = new PawnAnimation();
        a.PlayAnim(2, Once10, 1f, 0f, loop: false);
        a.EnableChannelNotify(2, false);
        var ends = new List<int>();
        for (int i = 0; i < 60; i++)
        {
            a.Advance(Dt, ends.Add);
        }

        Assert.Empty(ends);
        Assert.False(a.IsAnimating(2));
        Assert.Equal(a[2].AnimLast, a[2].Frame);
    }

    [Fact]
    public void TheTimeLeftAtAOneShotsEndCarriesIntoTheClipItsAnimEndStarts()
    {
        var a = new PawnAnimation();
        a.PlayAnim(0, Once10, 1f, 0f, loop: false);
        a.Advance(Dt, _ => { });
        a.SetFrame(0, 0.89f);
        a.Advance(Dt, ch => a.PlayAnim(ch, Loop8, 1f, 0f, loop: true));
        float left = Dt - ((0.9f - 0.89f) / 1.5f);
        Assert.Equal(Loop8.Name, a[0].Sequence);
        Assert.Equal(PawnAnimation.StartFrame + left, a[0].Frame, 4);
    }

    [Fact]
    public void ASpeedScaledRateAdvancesAtLeastPointThreePerSecond()
    {
        var a = new PawnAnimation();
        a.PlayAnim(0, Loop8, 1f, 0f, loop: true);
        a.Advance(Dt, _ => { });
        a.SetChannelRateScale(0, -1f);
        Assert.True(a[0].Rate < 0f);
        float before = a[0].Frame;
        a.Advance(0.1f, _ => { });
        Assert.Equal(before + 0.03f, a[0].Frame, 5);
    }

    [Fact]
    public void ATimedBlendReachesItsTargetAndAZeroTargetStopsTheChannel()
    {
        var a = new PawnAnimation();
        a.PlayAnim(4, Loop8, 1f, 0f, loop: true);
        a.SetBlendParams(4, 1f);
        a.BlendToAlpha(4, 0f, 0.1f);
        for (int i = 0; i < 12; i++)
        {
            a.Advance(Dt, _ => { });
        }

        Assert.Equal(0f, a[4].BlendAlpha);
        Assert.False(a[4].bTimedBlend);
        Assert.Equal(0f, a[4].Rate);
    }

    [Fact]
    public void AQueuedClipStartsWhenTheOneShotEnds()
    {
        var a = new PawnAnimation();
        a.PlayAnim(1, Once10, 1f, 0f, loop: false);
        a.PlayAnimAfter(1, Other, 1f, 0f);
        for (int i = 0; i < 60 && a[1].Sequence == Once10.Name; i++)
        {
            a.Advance(Dt, _ => { });
        }

        Assert.Equal(Other.Name, a[1].Sequence);
    }

    [Fact]
    public void ChannelZeroIgnoresBlendParams()
    {
        var a = new PawnAnimation();
        Assert.False(a.SetBlendParams(0, 1f));
        Assert.Equal(0f, a[0].BlendAlpha);
        Assert.True(a.SetBlendParams(14, 2f, 3f, 0.5f, new Name("B Spine1"), globalPose: true));
        Assert.Equal(2f, a[14].BlendAlpha);
        Assert.Equal(1f, a[14].BlendInTime);
        Assert.Equal(0.5f, a[14].BlendOutTime);
        Assert.Equal(new Name("B Spine1"), a[14].StartBone);
        Assert.True(a[14].bGlobalPose);
    }

    // ---- walk blender helpers ----------------------------------------------------------------------------------

    [Fact]
    public void StepBlendAlphaMovesByAtMostTheStep()
    {
        var a = new PawnAnimation();
        a.StepBlendAlpha(5, 1f, 0.25f);
        Assert.Equal(0.25f, a[5].BlendAlpha);
        a.StepBlendAlpha(5, 0.3f, 0.25f);
        Assert.Equal(0.3f, a[5].BlendAlpha);
        a.SetBlendAlpha(5, 3f);
        Assert.Equal(1f, a[5].BlendAlpha);
    }

    [Fact]
    public void SetAnimSequenceKeepsTheFrameAndRateButTakesTheNewBaseRate()
    {
        var a = new PawnAnimation();
        a.PlayAnim(5, Loop8, 1f, 0f, loop: true);
        a.SetFrame(5, 0.4f);
        float rate = a[5].Rate;
        a.SetAnimSequence(5, Other);
        Assert.Equal(Other.Name, a[5].Sequence);
        Assert.Equal(0.4f, a[5].Frame);
        Assert.Equal(rate, a[5].Rate);
        a.SetChannelRateScale(5, 1f);
        Assert.Equal(15f / 12f, a[5].Rate, 6);
    }

    [Fact]
    public void CopyAnimationCarriesTheClipTimingAndAlpha()
    {
        var a = new PawnAnimation();
        a.PlayAnim(4, Loop8, 1f, 0f, loop: true);
        a.SetFrame(4, 0.6f);
        a.SetBlendAlpha(4, 0.7f);
        a.CopyAnimation(4, 9);
        Assert.Equal(Loop8.Name, a[9].Sequence);
        Assert.Equal(0.6f, a[9].Frame);
        Assert.Equal(0.7f, a[9].BlendAlpha);
        Assert.Equal(a[4].Rate, a[9].Rate);
        Assert.True(a[9].bLoop);
    }

    [Fact]
    public void ZeroMovementAlphaFadesAndThenFreezesTheRange()
    {
        var a = new PawnAnimation();
        a.PlayAnim(6, Loop8, 1f, 0f, loop: true);
        a.SetFrame(6, 0.5f);
        a.SetBlendAlpha(6, 0.1f);
        a.ZeroMovementAlpha(4, 8, 0.25f);
        Assert.Equal(0f, a[6].BlendAlpha);
        Assert.NotEqual(0f, a[6].Rate);
        a.ZeroMovementAlpha(4, 8, 0.25f);
        Assert.Equal(0f, a[6].Rate);
        Assert.Equal(0f, a[6].Frame);
    }

    // ---- tween cache and sampling ------------------------------------------------------------------------------

    [Fact]
    public void TheTweenWeightFollowsTheFrameThroughATween()
    {
        var a = new PawnAnimation();
        a.PlayAnim(0, Other, 1f, 0f, loop: false);
        a.UpdateTweenCache();
        a.PlayAnim(0, Once10, 1f, 0.1f, loop: false);
        a.UpdateTweenCache();
        Assert.Equal(0f, a[0].TweenWeight);
        for (int i = 0; i < 3; i++)
        {
            a.Advance(Dt, _ => { });
            a.UpdateTweenCache();
            Assert.Equal(1f + (a[0].Frame * 10f), a[0].TweenWeight, 4);
        }
    }

    [Fact]
    public void ABackwardClipRisesInFrameWhileItsSampledKeyRunsFromTheEnd()
    {
        var a = new PawnAnimation();
        a.PlayAnim(0, Once10, 1f, 0f, loop: false, backward: true);
        Assert.Equal(0f, a[0].Frame);
        Assert.Equal(a[0].AnimLast, a[0].ClipFrame);
        a.Advance(Dt, _ => { });
        a.Advance(Dt, _ => { });
        Assert.True(a[0].Frame > 0f);
        Assert.Equal(a[0].AnimLast - a[0].Frame, a[0].ClipFrame, 6);
    }
}
