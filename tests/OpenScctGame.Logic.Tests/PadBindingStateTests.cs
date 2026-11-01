using System.Collections.Generic;
using OpenScctGame.Options;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class PadBindingStateTests
{
    private static readonly IReadOnlyDictionary<string, string> Spy = new Dictionary<string, string>
    {
        ["A"] = "Action:use", ["B"] = "Action:crouch", ["Y"] = "Action:jump",
        ["LB"] = "Action:coop", ["RT"] = "Action:fire", ["Back"] = "Action:hack",
        ["B:Hold"] = "Action:longroll", ["LS+Y"] = "Action:gadget1", ["LS+B"] = "Action:gadget3",
        ["LB+Y"] = "Action:launch1", ["LB+Back"] = "Action:scoreboard",
    };

    [Fact]
    public void OrdinaryButtonsPressOnceAndRelease()
    {
        var state = new PadBindingState();
        Assert.Equal([Event("RT", "Action:fire", true)], state.Update(Spy, Down("RT"), 0));
        Assert.Empty(state.Update(Spy, Down("RT"), 0.1));
        Assert.Equal([Event("RT", "Action:fire", false)], state.Update(Spy, Down(), 0.1));
    }

    [Fact]
    public void SelectionSuppressesJumpAndSurvivesModifierReleaseWithoutLeakingBaseAction()
    {
        var state = new PadBindingState();
        Assert.Empty(state.Update(Spy, Down("LS"), 0));
        Assert.Equal([Event("LS+Y", "Action:gadget1", true)], state.Update(Spy, Down("LS", "Y"), 0.01));
        Assert.Equal([Event("LS+Y", "Action:gadget1", false)], state.Update(Spy, Down("Y"), 0.01));
        Assert.Empty(state.Update(Spy, Down("LS", "Y"), 0.01));
        Assert.Empty(state.Update(Spy, Down("LS"), 0.01));
        Assert.Equal([Event("LS+Y", "Action:gadget1", true)], state.Update(Spy, Down("LS", "Y"), 0.01));
    }

    [Fact]
    public void ModifierCanSelectAgainAfterFaceButtonReleased()
    {
        var state = new PadBindingState();
        state.Update(Spy, Down("LS", "Y"), 0);
        Assert.Equal([Event("LS+Y", "Action:gadget1", false)], state.Update(Spy, Down("LS"), 0.01));
        Assert.Equal([Event("LS+B", "Action:gadget3", true)], state.Update(Spy, Down("LS", "B"), 0.01));
        Assert.Empty(state.Update(Spy, Down("LS", "B"), 1));
        Assert.Equal([Event("LS+B", "Action:gadget3", false)], state.Update(Spy, Down(), 0.01));
    }

    [Fact]
    public void SelectionWinsWhenBothModifiersArriveTogether()
    {
        var state = new PadBindingState();
        Assert.Equal([Event("LS+Y", "Action:gadget1", true)], state.Update(Spy, Down("LB", "LS", "Y"), 0));
        Assert.Equal([Event("LS+Y", "Action:gadget1", false)], state.Update(Spy, Down("LB", "Y"), 0.01));
        Assert.Empty(state.Update(Spy, Down(), 0.01));
    }

    [Fact]
    public void UnusedCoopModifierTapsOnReleaseButCombinationNeverTriggersCoop()
    {
        var state = new PadBindingState();
        Assert.Empty(state.Update(Spy, Down("LB"), 0));
        Assert.Equal([Event("LB", "Action:coop", true), Event("LB", "Action:coop", false)], state.Update(Spy, Down(), 0.1));
        Assert.Equal([Event("LB+Back", "Action:scoreboard", true)], state.Update(Spy, Down("LB", "Back"), 0));
        Assert.Equal([Event("LB+Back", "Action:scoreboard", false)], state.Update(Spy, Down("LB"), 0.1));
        Assert.Empty(state.Update(Spy, Down(), 0.1));
    }

    [Fact]
    public void ClutchIsImmediateAndStaysHeldDuringCombination()
    {
        var bindings = new Dictionary<string, string> { ["LB"] = "Clutch1", ["Back"] = "Action:hack", ["LB+Back"] = "Action:scoreboard" };
        var state = new PadBindingState();
        Assert.Equal([Event("LB", "Clutch1", true)], state.Update(bindings, Down("LB"), 0));
        Assert.Equal([Event("LB+Back", "Action:scoreboard", true)], state.Update(bindings, Down("LB", "Back"), 0.1));
        Assert.Equal([Event("LB+Back", "Action:scoreboard", false)], state.Update(bindings, Down("LB"), 0.1));
        Assert.Equal([Event("LB", "Clutch1", false)], state.Update(bindings, Down(), 0.1));
    }

    [Fact]
    public void CrouchTapAndLongRollAreMutuallyExclusive()
    {
        var state = new PadBindingState();
        Assert.Empty(state.Update(Spy, Down("B"), 0));
        Assert.Equal([Event("B", "Action:crouch", true), Event("B", "Action:crouch", false)], state.Update(Spy, Down(), 0.1));
        Assert.Empty(state.Update(Spy, Down("B"), 0));
        Assert.Empty(state.Update(Spy, Down("B"), PadBindingState.HoldSeconds - 0.01));
        Assert.Equal([Event("B:Hold", "Action:longroll", true)], state.Update(Spy, Down("B"), 0.011));
        Assert.Empty(state.Update(Spy, Down("B"), 1));
        Assert.Equal([Event("B:Hold", "Action:longroll", false)], state.Update(Spy, Down(), 0.01));
    }

    [Fact]
    public void LongPressCanBeBoundWithoutATapAction()
    {
        var bindings = new Dictionary<string, string> { ["B"] = "", ["B:Hold"] = "Action:longroll" };
        var state = new PadBindingState();
        Assert.Empty(state.Update(bindings, Down("B"), 0));
        Assert.Equal([Event("B:Hold", "Action:longroll", true)], state.Update(bindings, Down("B"), PadBindingState.HoldSeconds));
        Assert.Equal([Event("B:Hold", "Action:longroll", false)], state.Update(bindings, Down(), 0));
    }

    [Fact]
    public void HoldThresholdReachedOnReleaseStillPerformsOnlyLongRoll()
    {
        var state = new PadBindingState();
        Assert.Empty(state.Update(Spy, Down("B"), 0));
        Assert.Equal([Event("B:Hold", "Action:longroll", true), Event("B:Hold", "Action:longroll", false)],
            state.Update(Spy, Down(), PadBindingState.HoldSeconds));
    }

    [Fact]
    public void ReleaseAllDropsPendingTapsAndReleasesEveryActiveAction()
    {
        var state = new PadBindingState();
        state.Update(Spy, Down("RT", "LB", "B"), 0);
        Assert.Equal([Event("RT", "Action:fire", false)], state.ReleaseAll());
        Assert.Empty(state.Update(Spy, Down(), 0.1));
    }

    [Fact]
    public void ResetBlocksButtonsHeldAcrossFocusOrRoleChangeUntilReleased()
    {
        var state = new PadBindingState();
        state.Update(Spy, Down("RT", "LS", "Y"), 0);
        state.Reset(Down("RT", "LS", "Y"));
        Assert.Empty(state.Update(Spy, Down("RT", "LS", "Y"), 1));
        Assert.Empty(state.Update(Spy, Down("LS"), 0));
        Assert.Equal([Event("Y", "Action:jump", true)], state.Update(Spy, Down("LS", "Y"), 0));
        state.Reset();
        Assert.Equal([Event("RT", "Action:fire", true)], state.Update(Spy, Down("RT"), 0));
    }

    [Fact]
    public void ModifierAddedToHeldFaceReleasesBaseBeforeCombinationPress()
    {
        var state = new PadBindingState();
        Assert.Equal([Event("Y", "Action:jump", true)], state.Update(Spy, Down("Y"), 0));
        Assert.Equal([Event("Y", "Action:jump", false), Event("LS+Y", "Action:gadget1", true)], state.Update(Spy, Down("LS", "Y"), 0.01));
    }

    private static HashSet<string> Down(params string[] controls) => new(controls);
    private static PadBindingEvent Event(string control, string action, bool pressed) => new(control, action, pressed);
}
