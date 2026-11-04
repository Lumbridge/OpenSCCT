using System;
using System.Collections.Generic;
using System.Linq;
using OpenScctGame.Controls;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class BindingInputStateTests
{
    private static KeyLayout Layout() => KeyLayout.Parse("""
        [Engine.Input]
        IK_K=Plain
        IK_Ctrl+K=Chord
        IK_Shift=ModifierAction
        IK_Ctrl+Shift+K=Combined
        IK_LeftMouse=Fire
        IK_Ctrl+LeftMouse=Alternate
        IK_Ctrl+MouseWheelUp=WheelAction
        """);

    [Theory]
    [InlineData("Shift+Ctrl+K", "Ctrl+Shift+K")]
    [InlineData("Alt+Ctrl+Shift+MouseX1", "Ctrl+Shift+Alt+MouseX1")]
    [InlineData("Ctrl", "Ctrl")]
    [InlineData("NumPad1", "NumPad1")]
    public void ChordsHaveCanonicalModifierOrderAndPlainInputsStayCompatible(string input, string canonical)
    {
        Assert.Equal(canonical, BindingKey.Parse(input).Canonical);
        var layout = KeyLayout.Parse("[Engine.Input]\nIK_" + input + "=CustomCommand mode=keep");
        Assert.Contains(canonical, layout.Bindings.Keys);
        Assert.Equal(layout.ToIni(), KeyLayout.Parse(layout.ToIni()).ToIni());
    }

    [Theory]
    [InlineData("Ctrl+Ctrl+K")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Meta+K")]
    [InlineData("Ctrl+")]
    public void InvalidChordSpellingsAreRejectedWithoutErasingUnknownSavedKeys(string input)
    {
        Assert.False(BindingKey.TryParse(input, out _));
        Assert.Contains(input, KeyLayout.Parse("[Engine.Input]\nIK_" + input + "=Custom").Bindings.Keys);
    }

    [Fact]
    public void SelectionRequiresExactModifiersAndOtherwiseFallsBackToPlain()
    {
        var layout = Layout();
        Assert.Equal("Ctrl+K", BindingKey.Select(layout, "K", BindingModifiers.Ctrl));
        Assert.Equal("Ctrl+Shift+K", BindingKey.Select(layout, "K", BindingModifiers.Shift | BindingModifiers.Ctrl));
        Assert.Equal("K", BindingKey.Select(layout, "K", BindingModifiers.Ctrl | BindingModifiers.Alt));
        Assert.Null(BindingKey.Select(layout, "J", BindingModifiers.Ctrl));
    }

    [Fact]
    public void BaseFirstNeverPromotesAndRequiredModifierReleaseNeverStartsPlainBinding()
    {
        var state = new BindingInputState();
        var layout = Layout();
        Assert.Equal(new[] { new BindingInputEvent("K", true) }, state.Update(layout, "K", true));
        Assert.Empty(state.Update(layout, "Ctrl", true));
        Assert.Equal(new[] { "K" }, state.ActiveKeys);
        Assert.Equal(new[] { new BindingInputEvent("K", false) }, state.Update(layout, "K", false, BindingModifiers.Ctrl));
        Assert.Equal(new[] { new BindingInputEvent("Ctrl+K", true) }, state.Update(layout, "K", true, BindingModifiers.Ctrl));
        Assert.Equal(new[] { new BindingInputEvent("Ctrl+K", false) }, state.Update(layout, "Ctrl", false));
        Assert.Empty(state.ActiveKeys);
        Assert.Empty(state.Update(layout, "Ctrl", true));
        Assert.Empty(state.Update(layout, "K", true, BindingModifiers.Ctrl));
        Assert.Empty(state.Update(layout, "K", false, BindingModifiers.Ctrl));
        Assert.Equal(new[] { new BindingInputEvent("Ctrl+K", true) }, state.Update(layout, "K", true, BindingModifiers.Ctrl));
    }

    [Fact]
    public void ModifierActionRemainsIndependentAndExtraModifiersDoNotChangeLatchedChord()
    {
        var state = new BindingInputState();
        var layout = Layout();
        Assert.Equal(new[] { new BindingInputEvent("Shift", true) }, state.Update(layout, "Shift", true, BindingModifiers.Shift));
        state.Update(layout, "Ctrl", true, BindingModifiers.Ctrl | BindingModifiers.Shift);
        Assert.Equal(new[] { new BindingInputEvent("Ctrl+Shift+K", true) },
            state.Update(layout, "K", true, BindingModifiers.Ctrl | BindingModifiers.Shift));
        state.Update(layout, "Alt", true, BindingModifiers.Ctrl | BindingModifiers.Shift | BindingModifiers.Alt);
        Assert.Contains("Ctrl+Shift+K", state.ActiveKeys);
        Assert.Equal(new[] { new BindingInputEvent("Ctrl+Shift+K", false) }, state.Update(layout, "K", false));
        Assert.Contains("Shift", state.ActiveKeys);
    }

    [Fact]
    public void MouseChordsAndRepeatedWheelPulsesUseTheSameOwnershipRules()
    {
        var state = new BindingInputState();
        var layout = Layout();
        state.Update(layout, "Ctrl", true);
        Assert.Equal(new[] { new BindingInputEvent("Ctrl+LeftMouse", true) },
            state.Update(layout, "LeftMouse", true, BindingModifiers.Ctrl));
        Assert.Equal(new[] { new BindingInputEvent("Ctrl+LeftMouse", false) }, state.Update(layout, "Ctrl", false));
        Assert.Empty(state.Update(layout, "LeftMouse", false));
        for (int i = 0; i < 2; i++)
            Assert.Equal(new[] { new BindingInputEvent("Ctrl+MouseWheelUp", true), new BindingInputEvent("Ctrl+MouseWheelUp", false) },
                state.Update(layout, "MouseWheelUp", true, BindingModifiers.Ctrl));
        Assert.DoesNotContain("MouseWheelUp", state.PhysicalDown);
    }

    [Fact]
    public void ResetReleasesOwnedActionsAndIgnoresHeldInputsAndModifiersUntilRelease()
    {
        var state = new BindingInputState();
        var layout = Layout();
        state.Update(layout, "Ctrl", true);
        state.Update(layout, "K", true, BindingModifiers.Ctrl);
        Assert.Equal(new[] { new BindingInputEvent("Ctrl+K", false) }, state.Reset());
        Assert.Empty(state.Update(layout, "K", true, BindingModifiers.Ctrl));
        state.Update(layout, "K", false, BindingModifiers.Ctrl);
        Assert.Equal(new[] { new BindingInputEvent("K", true) }, state.Update(layout, "K", true, BindingModifiers.Ctrl));
        state.Update(layout, "K", false, BindingModifiers.Ctrl);
        state.Update(layout, "Ctrl", false);
        state.Update(layout, "Ctrl", true, BindingModifiers.Ctrl);
        Assert.Equal(new[] { new BindingInputEvent("Ctrl+K", true) }, state.Update(layout, "K", true, BindingModifiers.Ctrl));
    }

    [Fact]
    public void CaptureTracksPhysicalInputsButCannotStartActionsOrLeakOnExit()
    {
        var state = new BindingInputState();
        var layout = Layout();
        Assert.Empty(state.Update(layout, "K", true, suppressed: true));
        Assert.Contains("K", state.PhysicalDown);
        Assert.Empty(state.Update(layout, "K", true));
        Assert.Empty(state.Update(layout, "K", false));
        Assert.Equal(new[] { new BindingInputEvent("K", true) }, state.Update(layout, "K", true));
        state.Reset();
        state.Reconcile(new HashSet<string>());
        Assert.Equal(new[] { new BindingInputEvent("K", true) }, state.Update(layout, "K", true));
    }

    [Fact]
    public void ModifierAlreadyHeldWhenFocusReturnsCannotActivateAChordUntilReleased()
    {
        var state = new BindingInputState();
        var layout = Layout();
        state.BlockHeld(["Ctrl"]);
        Assert.Equal(new[] { new BindingInputEvent("K", true) }, state.Update(layout, "K", true, BindingModifiers.Ctrl));
        state.Update(layout, "K", false, BindingModifiers.Ctrl);
        state.Update(layout, "Ctrl", false);
        state.Update(layout, "Ctrl", true, BindingModifiers.Ctrl);
        Assert.Equal(new[] { new BindingInputEvent("Ctrl+K", true) }, state.Update(layout, "K", true, BindingModifiers.Ctrl));
    }

    [Fact]
    public void AliasesCustomArgumentsAndChordOnlyHudTokensSurviveSnapshotRoundTrip()
    {
        var layout = KeyLayout.Parse("[Engine.Input]\nAliases[0]=(Command=\"Jump\",Alias=\"JumpAlias\")\nIK_Shift+Ctrl+K=JumpAlias\nIK_Alt+X=Custom mode=keep");
        var reloaded = KeyLayout.Parse(layout.ToIni());
        Assert.Equal("Ctrl+Shift+K", KeyTokens.KeyBoundTo(reloaded, "JumpAlias"));
        Assert.Equal("Custom mode=keep", reloaded.Bindings["Alt+X"]);
        Assert.Equal("Jump", reloaded.Resolve("Ctrl+Shift+K").Single().Name);
    }
}
