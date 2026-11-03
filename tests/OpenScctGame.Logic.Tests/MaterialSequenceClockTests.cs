using OpenScctGame.Render.Materials;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class MaterialSequenceClockTests
{
    [Fact]
    public void ShowAndFadeUseAuthoredDurationsAndPreviousItem()
    {
        var clock = new MaterialSequenceClock([new(2f, false), new(1f, true)]);
        Assert.Equal(new(0, 0, 1f), clock.Update(10f));
        Assert.Equal(new(0, 0, 1f), clock.Update(11f));
        Assert.Equal(new(0, 1, 128f / 255f), clock.Update(12.5f));
        Assert.Equal(new(0, 0, 1f), clock.Update(13.1f));
    }

    [Fact]
    public void FirstFadeComesFromLastItemAndZeroDurationDoesNotDivideByZero()
    {
        var clock = new MaterialSequenceClock([new(1f, true), new(0f, true), new(2f, false)]);
        Assert.Equal(new(2, 0, 0f), clock.Update(1f));
        Assert.Equal(new(2, 2, 1f), clock.Update(2f));
    }

    [Fact]
    public void NonLoopingSequenceHoldsLastAndBackwardSceneTimeRestarts()
    {
        var clock = new MaterialSequenceClock([new(1f, false), new(1f, true)], loop: false);
        clock.Update(1f);
        Assert.Equal(new(1, 1, 1f), clock.Update(100f));
        Assert.Equal(new(0, 0, 1f), clock.Update(2f));
    }

    [Fact]
    public void PauseStopAndResetFollowMaterialTriggerActions()
    {
        var clock = new MaterialSequenceClock([new(1f, false), new(1f, false)]);
        clock.Update(1f);
        clock.Trigger(2);
        Assert.Equal(new(0, 0, 1f), clock.Update(2.5f));
        clock.Trigger(2);
        Assert.Equal(new(1, 1, 1f), clock.Update(2.5f));
        clock.Trigger(1);
        Assert.Equal(new(0, 0, 1f), clock.Update(3f));
        clock.Trigger(3);
        Assert.True(clock.Paused);
        clock.Reset();
        Assert.False(clock.Paused);
    }

    [Fact]
    public void UnpauseIncludesTimeSinceLastUnpausedUpdate()
    {
        var clock = new MaterialSequenceClock([new(10f, false), new(10f, true)]);
        clock.Update(1f);
        clock.Update(3f);
        clock.Trigger(2);
        Assert.Equal(new(0, 0, 1f), clock.Update(12f));
        Assert.Equal(new(0, 0, 1f), clock.Update(16f));
        clock.Trigger(2);
        Assert.Equal(new(0, 1, 128f / 255f), clock.Update(16f));
    }
}
