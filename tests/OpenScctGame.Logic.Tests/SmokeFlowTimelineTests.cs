using System;
using System.Collections.Generic;
using OpenScctGame.Render;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class SmokeFlowTimelineTests
{
    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(144)]
    public void RenderRatesProduceTheSameFixedSimulationSteps(int renderRate)
    {
        var clock = new SmokeFlowTimeline();
        Assert.True(clock.Plan(0, 0, 32).Reset);
        var ticks = new List<int>();
        for (int frame = 1; frame <= renderRate * 2; frame++)
        {
            float now = frame / (float)renderRate;
            SmokeFlowStep plan = clock.Plan(now, 0, 32);
            Assert.False(plan.Reset); Assert.InRange(plan.Steps, 0, 2);
            for (int step = 0; step < plan.Steps; step++)
                ticks.Add((int)MathF.Round((plan.StartTime + step * plan.DeltaTime) * SmokeFlowTimeline.Hertz));
            // A second camera and duplicate render callbacks see exactly the same simulation boundary.
            SmokeFlowStep duplicate = clock.Plan(now, 0, 32);
            Assert.False(duplicate.Reset); Assert.Equal(0, duplicate.Steps);
        }
        Assert.Equal(60, ticks.Count);
        for (int tick = 0; tick < ticks.Count; tick++) Assert.Equal(tick, ticks[tick]);
    }

    [Fact]
    public void LateJoinAndLongStallReseedWithoutAWorkBacklog()
    {
        var clock = new SmokeFlowTimeline();
        SmokeFlowStep late = clock.Plan(105, 100, 32);
        Assert.True(late.Reset); Assert.Equal(0, late.Steps); Assert.Equal(105, late.StartTime);
        SmokeFlowStep hitch = clock.Plan(108, 100, 32);
        Assert.True(hitch.Reset); Assert.Equal(0, hitch.Steps); Assert.Equal(108, hitch.StartTime);
        Assert.Equal(0, clock.Plan(108, 100, 32).Steps);
        SmokeFlowStep next = clock.Plan(108.04f, 100, 32);
        Assert.False(next.Reset); Assert.Equal(1, next.Steps);
    }

    [Fact]
    public void RewindResolutionAndReusedCloudIdentityDiscardOldState()
    {
        var clock = new SmokeFlowTimeline();
        clock.Plan(3, 0, 32);
        Assert.True(clock.Plan(2.99f, 0, 32).Reset);
        Assert.True(clock.Plan(2.99f, 0, 48).Reset);
        Assert.True(clock.Plan(2.99f, 1, 48).Reset);
        Assert.True(clock.Plan(2.99f, 1, 48, forceReset: true).Reset);
        Assert.False(clock.Plan(2.99f, 1, 48).Reset);
    }

    [Fact]
    public void TwoStepsAreAllowedButThreeReseed()
    {
        var clock = new SmokeFlowTimeline(); clock.Plan(0, 0, 32);
        SmokeFlowStep two = clock.Plan(2f / 30, 0, 32);
        Assert.False(two.Reset); Assert.Equal(2, two.Steps);
        SmokeFlowStep three = clock.Plan(5f / 30, 0, 32);
        Assert.True(three.Reset); Assert.Equal(0, three.Steps);
    }

    [Fact]
    public void FutureCreationDoesNotIntegrateNegativeAge()
    {
        var clock = new SmokeFlowTimeline();
        SmokeFlowStep initial = clock.Plan(1, 2, 32);
        Assert.True(initial.Reset); Assert.Equal(0, initial.Steps); Assert.Equal(2, initial.StartTime);
        Assert.Equal(0, clock.Plan(2, 2, 32).Steps);
        Assert.Equal(1, clock.Plan(2.04f, 2, 32).Steps);
    }

    [Fact]
    public void InvalidClockInputsCannotBecomeUnboundedDispatches()
    {
        var clock = new SmokeFlowTimeline();
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Plan(float.NaN, 0, 32));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Plan(0, float.PositiveInfinity, 32));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Plan(0, 0, 0));
    }
}
