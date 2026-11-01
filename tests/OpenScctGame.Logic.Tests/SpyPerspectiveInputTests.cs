using System;
using OpenScct.Game.SBase;
using OpenScctGame.Controls;
using OpenScctGame.Options;
using OpenScctGame.Play.Session;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class SpyPerspectiveInputTests
{
    [Fact]
    public void ReleasingPerspectiveDuringFreeCameraClearsTheSuspendedPreference()
    {
        var spy = new SPawnAttaque { Health = 100 };
        var controller = new SPlayerController { Pawn = spy, SBPawn = spy, FreeCameraFlying = 1 };
        controller.SetSpyFirstPerson(true);
        Assert.True(controller.SpyFirstPersonRequested);
        Assert.True(PlayerCommands.Execute(controller, new InputCommand("SetSpyFirstPerson", 0)));
        Assert.False(controller.SpyFirstPersonRequested);
        controller.FreeCameraFlying = 0;
        Assert.False(controller.SpyFirstPersonRequested);
    }

    [Fact]
    public void ToggleChangesOnlyOnPressAndClearRestoresThirdPerson()
    {
        var state = new InputActionState();
        Assert.True(state.SpyPerspective("X", true, hold: false, pulse: false));
        Assert.True(state.SpyPerspective("X", false, hold: false, pulse: false));
        Assert.False(state.SpyPerspective("X", true, hold: false, pulse: false));
        Assert.False(state.SpyPerspective("X", false, hold: false, pulse: false));
        state.SpyPerspective("X", true, hold: false, pulse: false);
        state.Clear();
        Assert.False(state.SpyFirstPersonRequested);
    }

    [Fact]
    public void HoldRequiresEveryKeyboardAndPadSourceToRelease()
    {
        var state = new InputActionState();
        Assert.True(state.SpyPerspective("X", true, hold: true, pulse: false));
        Assert.True(state.SpyPerspective("svm-action:RB:perspective", true, hold: true, pulse: false));
        Assert.True(state.SpyPerspective("x", false, hold: true, pulse: false));
        Assert.False(state.SpyPerspective("svm-action:RB:perspective", false, hold: true, pulse: false));
        state.SpyPerspective("X", true, hold: true, pulse: false);
        state.Clear();
        Assert.False(state.SpyFirstPersonRequested);
        Assert.False(state.SpyPerspective("X", false, hold: true, pulse: false));
    }

    [Fact]
    public void WheelAssignmentsToggleEvenWithHoldBehaviorAndDoNotAffectOtherActions()
    {
        var state = new InputActionState();
        state.Walk("Shift", true, hold: true, pulse: false);
        state.Scope("Scope", true);
        Assert.True(state.SpyPerspective("MouseWheelUp", true, hold: true, pulse: true));
        Assert.False(state.SpyPerspective("MouseWheelUp", true, hold: true, pulse: true));
        Assert.True(state.ScopeHeld);
        Assert.True(state.Walk("Shift", true, hold: true, pulse: false));
    }

    [Fact]
    public void OldSettingsDefaultToToggleAndHoldSurvivesRoundTrip()
    {
        GameSettings defaults = GameSettings.FromJson("{}");
        Assert.Equal("Toggle", defaults.SpyKbm.PerspectiveBehavior);
        defaults.SpyKbm.PerspectiveBehavior = "Hold";
        GameSettings loaded = GameSettings.FromJson(defaults.ToJson());
        Assert.Equal("Hold", loaded.SpyKbm.PerspectiveBehavior);
        loaded.SpyKbm.PerspectiveBehavior = "invalid";
        loaded.Clamp();
        Assert.Equal("Toggle", loaded.SpyKbm.PerspectiveBehavior);
    }

    [Fact]
    public void ActionIsSpyOnlyAndCanBeAssignedToAControllerIndependently()
    {
        ControlAction action = Assert.Single(ControlActions.ForRole(false), a => a.Id == "perspective");
        Assert.DoesNotContain(ControlActions.ForRole(true), a => a.Id == action.Id);
        Assert.True(ControlActions.Direct(action));
        KeyAction pad = Assert.Single(ControlActions.VirtualActions(ControlActions.VirtualPrefix + "RB:perspective"));
        Assert.Equal("SwitchSpyPerspective", pad.Name);
        Assert.Equal(KeyActionKind.Command, pad.Kind);
    }

    [Fact]
    public void DefaultAndOldSnapshotsGainXWithoutDisplacingCustomBindings()
    {
        KeyLayout stock = DefaultKeyLayout.Create();
        Assert.Equal("SwitchSpyPerspective", Assert.Single(DefaultKeyLayout.ApplyProfile(stock, null, "Gadget").Resolve("X")).Name);
        string oldSnapshot = stock.ToIni().Replace(DefaultKeyLayout.PerspectiveBindingMarker + "\n", "", StringComparison.Ordinal);
        Assert.Equal("SwitchSpyPerspective", Assert.Single(DefaultKeyLayout.ApplyProfile(stock, oldSnapshot, "Gadget").Resolve("X")).Name);
        KeyLayout custom = KeyLayout.Parse(oldSnapshot);
        custom.Bind("X", "CustomCommand 7");
        string occupied = custom.ToIni().Replace(DefaultKeyLayout.PerspectiveBindingMarker + "\n", "", StringComparison.Ordinal);
        Assert.Equal("CustomCommand", Assert.Single(DefaultKeyLayout.ApplyProfile(stock, occupied, "Gadget").Resolve("X")).Name);
        custom.Bind("J", "SwitchSpyPerspective");
        custom.Bind("X", "");
        string rebound = custom.ToIni().Replace(DefaultKeyLayout.PerspectiveBindingMarker + "\n", "", StringComparison.Ordinal);
        KeyLayout loaded = DefaultKeyLayout.ApplyProfile(stock, rebound, "Gadget");
        Assert.Empty(loaded.Resolve("X"));
        Assert.Equal("SwitchSpyPerspective", Assert.Single(loaded.Resolve("J")).Name);
    }

    [Fact]
    public void DeliberatelyUnboundPerspectiveStaysUnboundAfterReload()
    {
        KeyLayout stock = DefaultKeyLayout.Create();
        KeyLayout layout = DefaultKeyLayout.ApplyProfile(stock, null, "Gadget");
        layout.Bind("X", "");
        string saved = layout.ToIni();
        KeyLayout reloaded = DefaultKeyLayout.ApplyProfile(stock, saved, "Gadget");
        Assert.Empty(reloaded.Resolve("X"));
        Assert.Equal(saved, reloaded.ToIni());
    }
}
