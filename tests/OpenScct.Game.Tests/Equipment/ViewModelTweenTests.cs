using System.Collections.Generic;
using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using Xunit;

namespace OpenScct.Game.Tests.Equipment;

public sealed class ViewModelTweenTests
{
    private static readonly Name Landing = ViewModelClips.FirstLandStFd2;
    private static readonly ClipData Clip = K.FirstLandStFd2;

    [Theory]
    [InlineData(1f, false)]
    [InlineData(2f, false)]
    [InlineData(1f, true)]
    public void EntryTweenHoldsPlaybackThenUsesTheRemainingTimeInTheTick(float rate, bool backward)
    {
        var animation = new ViewModelAnim();
        var owner = new SWeaponDefense();
        var ended = new List<int>();
        animation.Play(0, Landing, Clip, rate, backward, TweenTime: 0.2f);

        animation.Advance(0.1f, owner, (_, channel) => ended.Add(channel));
        Assert.Equal(0f, animation.Elapsed(0, Clip));
        Assert.Equal(Clip.Seconds(rate) + 0.1f, animation.Remaining(0), 5);
        Assert.True(animation.IsAnimating(0));
        Assert.Equal(backward, animation.IsBackward(0));
        Assert.Empty(ended);

        animation.Advance(0.15f, owner, (_, channel) => ended.Add(channel));
        Assert.Equal(0.05f, animation.Elapsed(0, Clip), 5);
        Assert.Equal(Clip.Seconds(rate) - 0.05f, animation.Remaining(0), 5);
        Assert.Empty(ended);

        animation.Advance(animation.Remaining(0) - 0.01f, owner, (_, channel) => ended.Add(channel));
        Assert.True(animation.IsAnimating(0));
        Assert.Empty(ended);
        animation.Advance(0.02f, owner, (_, channel) => ended.Add(channel));
        Assert.False(animation.IsAnimating(0));
        Assert.Equal(0f, animation.Remaining(0));
        Assert.Equal(new[] { 0 }, ended);
        animation.Advance(1f, owner, (_, channel) => ended.Add(channel));
        Assert.Equal(new[] { 0 }, ended);
    }

    [Fact]
    public void ReplayingTheSameLandingRestartsItsEntryTween()
    {
        var animation = new ViewModelAnim();
        var owner = new SWeaponDefense();
        animation.Play(0, Landing, Clip, TweenTime: 0.2f);
        animation.Advance(0.3f, owner, (_, _) => Assert.Fail("Landing ended before its playback completed"));
        Assert.Equal(0.1f, animation.Elapsed(0, Clip), 5);

        animation.Play(0, Landing, Clip, TweenTime: 0.2f);
        Assert.Equal(0f, animation.Elapsed(0, Clip));
        Assert.Equal(Clip.Seconds(1f) + 0.2f, animation.Remaining(0), 5);
        animation.Advance(0.15f, owner, (_, _) => Assert.Fail("Restarted landing ended during its entry tween"));
        Assert.Equal(0f, animation.Elapsed(0, Clip));
        animation.Advance(0.1f, owner, (_, _) => Assert.Fail("Restarted landing ended before its playback completed"));
        Assert.Equal(0.05f, animation.Elapsed(0, Clip), 5);
    }

    [Fact]
    public void SeekingIntoTheClipSkipsThePendingEntryTween()
    {
        var animation = new ViewModelAnim();
        var owner = new SWeaponDefense();
        int ends = 0;
        animation.Play(4, Landing, Clip, Rate: 2f, TweenTime: 0.2f);

        animation.SetFrame(4, 6f, Clip);
        Assert.Equal(6f / 30f, animation.Elapsed(4, Clip), 5);
        Assert.Equal(4f / 30f, animation.Remaining(4), 5);
        animation.Advance(0.05f, owner, (_, _) => ends++);
        Assert.Equal(0.25f, animation.Elapsed(4, Clip), 5);
        Assert.Equal(0, ends);
        animation.Advance(0.1f, owner, (_, _) => ends++);
        Assert.False(animation.IsAnimating(4));
        Assert.Equal(1, ends);
    }

    [Fact]
    public void LoopAndStopDiscardPendingTweenWithoutRaisingAnimationEnd()
    {
        var animation = new ViewModelAnim();
        var owner = new SWeaponDefense();
        int ends = 0;
        animation.Play(0, Landing, Clip, Backward: true, TweenTime: 0.2f);
        animation.Loop(0, ViewModelClips.FirstWaitStFd2);
        animation.Advance(3f, owner, (_, _) => ends++);
        Assert.True(animation.IsAnimating(0));
        Assert.False(animation.IsBackward(0));
        Assert.Equal(ViewModelClips.FirstWaitStFd2, animation.Sequence(0));
        Assert.Equal(0f, animation.TweenTime(0));
        Assert.Equal(0f, animation.Elapsed(0, Clip));
        Assert.Equal(0, ends);

        animation.Play(0, Landing, Clip, TweenTime: 0.2f);
        animation.Stop(0);
        animation.Advance(3f, owner, (_, _) => ends++);
        Assert.False(animation.IsAnimating(0));
        Assert.Equal(0f, animation.Remaining(0));
        Assert.Equal(0f, animation.TweenTime(0));
        Assert.Equal(0f, animation.Elapsed(0, Clip));
        Assert.Equal(0, ends);
    }

    [Fact]
    public void ReplacingTweenedLandingWithAnUntweenedActionStartsPlaybackImmediately()
    {
        var animation = new ViewModelAnim();
        var owner = new SWeaponDefense();
        animation.Play(0, Landing, Clip, TweenTime: 0.2f);
        animation.Play(0, ViewModelClips.FirstShooStFd2, K.FirstShooStFd2);
        Assert.Equal(0f, animation.TweenTime(0));
        animation.Advance(0.05f, owner, (_, _) => Assert.Fail("Shot ended before its playback completed"));
        Assert.Equal(0.05f, animation.Elapsed(0, K.FirstShooStFd2), 5);
    }
}
