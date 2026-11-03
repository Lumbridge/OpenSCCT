using System.Collections.Generic;
using System.Linq;
using OpenScctGame.Render;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class MapSpotLightBudgetTests
{
    [Fact]
    public void NearerLightsBehindTheCameraCannotEvictAVisibleWindowPatch()
    {
        var budget = new MapSpotLightBudget(6);
        var candidates = Enumerable.Range(0, 6).Select(i => new MapSpotLightCandidate(i, i, false)).ToList();
        candidates.Add(new MapSpotLightCandidate(6, 2000f, true));

        budget.Update(candidates, 0.016f);

        Assert.Equal(6, budget.Active.Count);
        Assert.Equal(1f, budget.Active[6]);
    }

    [Fact]
    public void SmallMovementAroundTheDistanceBoundaryKeepsTheExistingLight()
    {
        var budget = new MapSpotLightBudget(1);
        budget.Update([new(0, 1000f, true), new(1, 1001f, true)], 0.016f);
        for (int frame = 0; frame < 120; frame++)
            budget.Update([new(0, 1000f, true), new(1, frame % 2 == 0 ? 999f : 1001f, true)], 0.016f);

        Assert.Equal(0, Assert.Single(budget.Active).Key);
        Assert.Equal(1f, budget.Active[0]);
    }

    [Fact]
    public void AFullBudgetFadesTheOldLightBeforeReusingItsSlotAndFadesInTheReplacement()
    {
        var budget = new MapSpotLightBudget(1);
        budget.Update([new(0, 100f, true), new(1, 1000f, true)], 0.016f);
        MapSpotLightCandidate[] moved = [new(0, 1000f, true), new(1, 100f, true)];
        var oldGains = new List<float>();
        var newGains = new List<float>();
        for (int frame = 0; frame < 30; frame++)
        {
            budget.Update(moved, 0.02f);
            Assert.Single(budget.Active);
            if (budget.Active.TryGetValue(0, out float oldGain)) oldGains.Add(oldGain);
            if (budget.Active.TryGetValue(1, out float newGain)) newGains.Add(newGain);
        }

        Assert.InRange(oldGains[0], 0.8f, 0.9f);
        Assert.True(oldGains.Count > 1);
        Assert.True(oldGains.SequenceEqual(oldGains.OrderDescending()));
        Assert.Equal(0f, newGains[0]);
        Assert.Contains(newGains, gain => gain > 0f && gain < 1f);
        Assert.True(newGains.SequenceEqual(newGains.Order()));
        Assert.Equal(1f, budget.Active[1]);
    }

    [Fact]
    public void TurningAwayReleasesAnOffscreenOccupantWithoutWaitingForItsFade()
    {
        var budget = new MapSpotLightBudget(1);
        budget.Update([new(0, 10f, true), new(1, 1000f, false)], 0.016f);
        budget.Update([new(0, 10f, false), new(1, 1000f, true)], 0.016f);

        Assert.Equal(1, Assert.Single(budget.Active).Key);
    }

    [Fact]
    public void SwitchingALightOffIsImmediateEvenWhileItsBudgetTransitionIsActive()
    {
        var budget = new MapSpotLightBudget(1);
        budget.Update([new(0, 100f, true)], 0.016f);
        budget.Update([], 0.016f);
        Assert.Empty(budget.Active);
    }

    [Fact]
    public void ALongFrameDoesNotTurnABudgetTransitionIntoAnInstantSwitch()
    {
        var budget = new MapSpotLightBudget(1);
        budget.Update([new(0, 100f, true), new(1, 1000f, true)], 0.016f);
        budget.Update([new(0, 1000f, true), new(1, 100f, true)], 3f);

        Assert.InRange(budget.Active[0], 0.6f, 0.7f);
    }
}
