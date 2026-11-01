using System.Linq;
using OpenScctGame.Options;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class PadActionStateTests
{
    [Fact]
    public void SpyFirePressesImmediatelyAndQuickUsesOnlyOnShortRelease()
    {
        var state = new PadActionState();
        Assert.Equal([E("RT", "Action:fire", true)], state.Update([E("RT", "Pad:spyfire", true)], 0, new()));
        Assert.Equal([E("RT", "Action:fire", false), E("RT:tap", "Action:quick1", true), E("RT:tap", "Action:quick1", false)],
            state.Update([E("RT", "Pad:spyfire", false)], 0.1, new()));
        state.Update([E("RT", "Pad:spyfire", true)], 0, new());
        Assert.Equal([E("RT", "Action:fire", false)], state.Update([E("RT", "Pad:spyfire", false)], 0.2, new()));
    }

    [Fact]
    public void SpyGadgetTapQuickUsesSecondSlotOnlyWhenUnarmed()
    {
        var state = new PadActionState();
        Assert.Equal([E("LT", "Action:altfire", true)], state.Update([E("LT", "Pad:spygadget", true)], 0, new()));
        Assert.Equal(["Action:altfire", "Action:quick2", "Action:quick2"],
            state.Update([E("LT", "Pad:spygadget", false)], 0.1, new()).Select(e => e.Action));
        state.Update([E("LT", "Pad:spygadget", true)], 0, new(WeaponDrawn: true));
        Assert.Equal([E("LT", "Action:altfire", false)],
            state.Update([E("LT", "Pad:spygadget", false)], 0.1, new(WeaponDrawn: true)));
    }

    [Fact]
    public void CloakHoldFiresOnceAndReleaseDoesNotToggleAgain()
    {
        var state = new PadActionState();
        state.Update([E("LT", "Pad:spygadget", true)], 0, new(CloakSelected: true));
        Assert.Empty(state.Update([], 0.19, new(CloakSelected: true)));
        Assert.Equal([E("LT:hold", "Action:cloakquick", true), E("LT:hold", "Action:cloakquick", false)],
            state.Update([], 0.02, new(CloakSelected: true)));
        Assert.Empty(state.Update([], 0.3, new(CloakSelected: true, CloakActive: true)));
        Assert.Equal([E("LT", "Action:altfire", false)], state.Update([E("LT", "Pad:spygadget", false)], 0, new(CloakSelected: true, CloakActive: true)));
    }

    [Fact]
    public void ReleaseCrossingHoldThresholdInvokesOnlyTheHoldCallback()
    {
        var state = new PadActionState();
        state.Update([E("LT", "Pad:spygadget", true)], 0, new(CloakSelected: true));
        Assert.Equal(["Action:cloakquick", "Action:cloakquick", "Action:altfire"],
            state.Update([E("LT", "Pad:spygadget", false)], 0.2, new(CloakSelected: true, CloakActive: true)).Select(e => e.Action));
    }

    [Fact]
    public void ActiveSelectedCloakTapTurnsOffWithoutSelectingSecondSlot()
    {
        var state = new PadActionState();
        state.Update([E("LT", "Pad:spygadget", true)], 0, new());
        Assert.Equal(["Action:altfire", "Action:cloakquick", "Action:cloakquick"],
            state.Update([E("LT", "Pad:spygadget", false)], 0.1, new(CloakSelected: true, CloakActive: true)).Select(e => e.Action));
    }

    [Fact]
    public void SecondSlotCloakSelectsThenUsesBeforeQuickChoice()
    {
        var state = new PadActionState();
        state.Update([E("LT", "Pad:spygadget", true)], 0, new());
        Assert.Equal(["Action:slot2", "Action:cloakquick", "Action:quick2"],
            state.Update([E("LT", "Pad:spygadget", false)], 0.1, new(Slot2IsCloak: true)).Where(e => e.Pressed).Select(e => e.Action));
    }

    [Fact]
    public void CloakSelectedElsewhereSelectsSecondSlotBeforeQuickChoice()
    {
        var state = new PadActionState();
        state.Update([E("LT", "Pad:spygadget", true)], 0, new());
        Assert.Equal(["Action:slot2", "Action:quick2"],
            state.Update([E("LT", "Pad:spygadget", false)], 0.1, new(CloakSelected: true)).Where(e => e.Pressed).Select(e => e.Action));
    }

    [Fact]
    public void CameraGadgetReleaseCannotThrowWhenItExitsCamera()
    {
        var state = new PadActionState();
        state.Update([E("LT", "Pad:spygadget", true)], 0, new(InCamera: true));
        Assert.Equal([E("LT", "Action:altfire", false)], state.Update([E("LT", "Pad:spygadget", false)], 0.1, new()));
        state.Update([], 0.2, new());
        state.Update([E("LT", "Pad:spygadget", true)], 0, new());
        Assert.Contains(E("LT:tap", "Action:quick2", true), state.Update([E("LT", "Pad:spygadget", false)], 0.1, new()));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CameraFireUsesGasInsteadOfWeaponFire(bool camera, bool transition)
    {
        var state = new PadActionState();
        Assert.Equal([E("RT:camera", "Action:cameragas", true), E("RT:camera", "Action:cameragas", false)],
            state.Update([E("RT", "Pad:spyfire", true)], 0, new(InCamera: camera, CameraTransition: transition)));
        Assert.Empty(state.ReleaseAll());
    }

    [Fact]
    public void MercGadgetHoldsBreathOnlyIfScopedWhenPressedAndAlwaysReleasesIt()
    {
        var state = new PadActionState();
        Assert.Equal([E("LT", "Action:altfire", true), E("LT:breath", "Action:breath", true)],
            state.Update([E("LT", "Pad:mercgadget", true)], 0, new(IsSpy: false, Scoped: true)));
        Assert.Equal([E("LT", "Action:altfire", false), E("LT:breath", "Action:breath", false)],
            state.Update([E("LT", "Pad:mercgadget", false)], 0.4, new(IsSpy: false)));
        Assert.Equal([E("LT", "Action:altfire", true)], state.Update([E("LT", "Pad:mercgadget", true)], 0, new(IsSpy: false)));
        Assert.Empty(state.Update([], 0.4, new(IsSpy: false, Scoped: true)));
    }

    [Fact]
    public void CameraCrouchExitsWithoutLeakingAStanceRequest()
    {
        var state = new PadActionState();
        Assert.Equal([E("B:camera", "Action:cameraexit", true), E("B:camera", "Action:cameraexit", false)],
            state.Update([E("B", "Pad:crouch", true), E("B", "Pad:crouch", false)], 0, new(InCamera: true)));
    }

    [Fact]
    public void CameraLongRollKeepsRollHeldAndExitsRegardlessOfCrouchLatch()
    {
        var state = new PadActionState();
        Assert.Equal([E("B:Hold", "Action:longroll", true), E("B:Hold:camera", "Action:cameraexit", true), E("B:Hold:camera", "Action:cameraexit", false)],
            state.Update([E("B:Hold", "Pad:longroll", true)], 0, new(InCamera: true)));
        Assert.Equal([E("B:Hold", "Action:longroll", false)],
            state.Update([E("B:Hold", "Pad:longroll", false)], 0.3, new()));
    }

    [Fact]
    public void CrouchTapAndLongRollRemainSeparateActionsOutsideCamera()
    {
        var state = new PadActionState();
        Assert.Equal([E("B", "Action:crouch", true), E("B", "Action:crouch", false)],
            state.Update([E("B", "Pad:crouch", true), E("B", "Pad:crouch", false)], 0, new()));
        Assert.Equal([E("B:Hold", "Action:longroll", true)], state.Update([E("B:Hold", "Pad:longroll", true)], 0, new()));
        Assert.Equal([E("B:Hold", "Action:longroll", false)], state.Update([E("B:Hold", "Pad:longroll", false)], 0.4, new()));
    }

    [Fact]
    public void AbortReleasesOrdinaryAndContextualActionsWithoutTapCommands()
    {
        var state = new PadActionState();
        state.Update([E("Y", "Action:jump", true), E("RT", "Pad:spyfire", true), E("LT", "Pad:mercgadget", true)], 0, new(Scoped: true));
        var releases = state.ReleaseAll();
        Assert.Equal(4, releases.Count);
        Assert.All(releases, ev => Assert.False(ev.Pressed));
        Assert.Empty(state.Update([E("RT", "Pad:spyfire", false), E("LT", "Pad:mercgadget", false)], 1, new()));
    }

    [Fact]
    public void ExplicitFireActionNeverAcquiresDefaultTriggerTapBehavior()
    {
        var state = new PadActionState();
        Assert.Equal([E("RT", "Action:fire", true), E("RT", "Action:fire", false)],
            state.Update([E("RT", "Action:fire", true), E("RT", "Action:fire", false)], 0, new()));
    }

    private static PadBindingEvent E(string control, string action, bool pressed) => new(control, action, pressed);
}
