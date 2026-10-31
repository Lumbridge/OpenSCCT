using System;
using System.Linq;
using OpenScctGame.Controls;
using OpenScctGame.UI;
using OpenScctGame.Options;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class ActionBindingDraftTests
{
    private static ControlAction Action(string id) => ControlActions.All.Single(a => a.Id == id);
    private const string Ini = """
        [Engine.Input]
        Aliases[0]=(Command="Button bAltFire | Function AltFire",Alias="AltFire")
        Aliases[1]=(Command="Button bHoldBreath",Alias="HoldBreath")
        IK_RightMouse=AltFire | GadgetQuickChoice 1 | HoldBreath | CustomThing mode=keep
        IK_R=Button bReload
        IK_MouseX=Axis aMouseX Speed=6.00
        """;

    [Theory]
    [InlineData("Gadget", "NextGadget", "PreviousGadget")]
    [InlineData("Speed", "Run", "Walk")]
    public void LegacyWheelMigrationIsIdempotentAndPreservesCustomCommands(string mode, string up, string down)
    {
        var layout = KeyLayout.Parse("[Engine.Input]\nIK_MouseWheelUp=WheelUp | CustomThing 12\nIK_MouseWheelDown=WheelDown\nIK_MiddleMouse=ChangeSpeed");
        layout.UpgradeWheelBindings(mode);
        Assert.Equal(up + " | CustomThing 12", layout.Bindings["MouseWheelUp"]);
        Assert.Equal(down, layout.Bindings["MouseWheelDown"]);
        Assert.Equal("Walk", layout.Bindings["MiddleMouse"]);
        string saved = layout.ToIni();
        layout.UpgradeWheelBindings(mode);
        Assert.Equal(saved, layout.ToIni());
    }

    [Fact]
    public void WalkAndScopeHoldSourcesAndToggleResetAreIndependent()
    {
        var state = new InputActionState();
        Assert.True(state.Walk("A", true, true, false));
        Assert.True(state.Walk("B", true, true, false));
        Assert.True(state.Walk("A", false, true, false));
        Assert.False(state.Walk("B", false, true, false));
        Assert.True(state.Walk("Wheel", true, true, true));
        Assert.False(state.Walk("Wheel", true, true, true));
        Assert.True(state.Walk("A", true, false, false));
        Assert.True(state.Walk("A", false, false, false));
        Assert.True(state.Scope("ScopeA", true));
        Assert.True(state.Scope("ScopeB", true));
        Assert.True(state.Scope("ScopeA", false));
        Assert.False(state.Scope("ScopeB", false));
        state.Clear();
        Assert.False(state.Walk("A", false, false, false));
    }

    [Fact]
    public void BehaviorChoicesRoundTripAndInvalidChoicesNormalize()
    {
        var settings = new GameSettings();
        settings.SpyKbm.WalkBehavior = "Hold";
        settings.MercKbm.ScopeBehavior = "Hold";
        var loaded = settings.Clone();
        Assert.Equal("Hold", loaded.SpyKbm.WalkBehavior);
        Assert.Equal("Hold", loaded.MercKbm.ScopeBehavior);
        loaded.MercKbm.WalkBehavior = "invalid";
        loaded.Clamp();
        Assert.Equal("Toggle", loaded.MercKbm.WalkBehavior);
    }

    [Fact]
    public void DraftDoesNotMutateSourceAndUnbindPreservesOtherActionsAndArguments()
    {
        KeyLayout source = KeyLayout.Parse(Ini);
        var draft = new ActionBindingDraft(source);
        draft.Remove(Action("breath"));
        Assert.Contains("HoldBreath", source.Bindings["RightMouse"]);
        Assert.DoesNotContain("bHoldBreath", draft.CommandOf("RightMouse"));
        Assert.Contains("CustomThing mode=keep", draft.CommandOf("RightMouse"));
        Assert.Contains("Function AltFire", draft.CommandOf("RightMouse"));
        Assert.True(draft.Dirty);
        draft.Accept();
        Assert.False(draft.Dirty);
    }

    [Fact]
    public void SplitActionCanUseAnUnoccupiedKeyAndSurvivesSnapshotRoundTrip()
    {
        var draft = new ActionBindingDraft(KeyLayout.Parse(Ini));
        draft.Assign(Action("breath"), "X");
        var reloaded = new ActionBindingDraft(KeyLayout.Parse(draft.Layout.ToIni()));
        Assert.Equal(new[] { "X" }, reloaded.Keys(Action("breath")));
        Assert.Contains("GadgetQuickChoice 1", reloaded.CommandOf("RightMouse"));
        Assert.Equal("Axis aMouseX Speed=6.00", reloaded.CommandOf("MouseX"));
        Assert.Equal(KeyLayout.Parse(Ini).Aliases, reloaded.Layout.Aliases);
    }

    [Fact]
    public void ShareReplaceAndSwapHaveExplicitBehavior()
    {
        var draft = new ActionBindingDraft(KeyLayout.Parse(Ini));
        draft.Assign(Action("breath"), "R");
        Assert.Contains("bReload", draft.CommandOf("R"));
        Assert.Contains("bHoldBreath", draft.CommandOf("R"));
        draft.Swap(Action("breath"), "R", "RightMouse");
        Assert.Equal("Button bHoldBreath", draft.CommandOf("RightMouse"));
        Assert.Contains("bReload", draft.CommandOf("R"));
        Assert.Contains("CustomThing mode=keep", draft.CommandOf("R"));
        draft.Assign(Action("breath"), "R", replace: true);
        Assert.Equal("Button bHoldBreath", draft.CommandOf("R"));
        Assert.Empty(draft.CommandOf("RightMouse"));
    }

    [Fact]
    public void UnboundActionsRemainInCatalogAndRolesHaveCorrectVisionAndFireModeNames()
    {
        Assert.Empty(new ActionBindingDraft(KeyLayout.Parse(Ini)).Keys(Action("quick1")));
        Assert.Equal("Night vision", ControlActions.ForRole(false).Single(a => a.Id == "vision1").Label);
        Assert.Equal("Motion tracking vision", ControlActions.ForRole(true).Single(a => a.Id == "vision1").Label);
        Assert.Equal("Switch fire mode", ControlActions.ForRole(true).Single(a => a.Id == "rof").Label);
        Assert.DoesNotContain(ControlActions.ForRole(true), a => a.Id == "ready");
    }

    [Fact]
    public void NormalActionListsOmitInactiveOriginalShortcuts()
    {
        foreach (bool merc in new[] { false, true })
            Assert.DoesNotContain(ControlActions.ForRole(merc), a => a.Id is "controls" or "hudhelp" or "quit");
        Assert.Contains(ControlActions.ForRole(false), a => a.Id == "breath" && a.Label.Contains("Cloak"));
        Assert.DoesNotContain(ControlActions.ForRole(true), a => a.Id == "roll");
    }

    [Fact]
    public void IndependentPadActionDoesNotRunOtherKeyboardActionsOrRequireAKey()
    {
        var actions = ControlActions.VirtualActions(ControlActions.VirtualPrefix + "RT:altfire");
        Assert.Contains(actions, a => a.Name == "bAltFire");
        Assert.Contains(actions, a => a.Name == "AltFire");
        Assert.DoesNotContain(actions, a => a.Name is "GadgetQuickChoice" or "bHoldBreath");
        Assert.Equal(1, ControlActions.VirtualActions(ControlActions.VirtualPrefix + "RB:quick2").Single().Arg);
    }

    [Fact]
    public void SnapshotKeepsClearedKeysClearedAndCustomAliasesAcrossLoads()
    {
        KeyLayout stock = KeyLayout.Parse(Ini);
        KeyLayout changed = stock.Clone();
        changed.Bind("RightMouse", "");
        changed.Bind("R", "");
        changed.Bind("Y", "AltFire");
        KeyLayout loaded = KeyLayout.ApplyOverrides(stock, changed.ToIni());
        Assert.False(loaded.Bindings.ContainsKey("R"));
        Assert.False(loaded.Bindings.ContainsKey("RightMouse"));
        Assert.Contains(loaded.Resolve("Y"), a => a.Name == "AltFire");
        Assert.Equal(changed.ToIni(), KeyLayout.ApplyOverrides(stock, loaded.ToIni()).ToIni());
    }

    [Fact]
    public void LegacyOverlayStillMovesStockKeysAndImportsCustomAliases()
    {
        const string user = """
            [Engine.Input]
            Aliases[0]=(Command="Button bFire | Function Fire",Alias="CustomFire")
            IK_Y=Button bReload
            IK_T=CustomFire
            """;
        KeyLayout loaded = KeyLayout.ApplyOverrides(KeyLayout.Parse(Ini), user);
        Assert.False(loaded.Bindings.ContainsKey("R"));
        Assert.Equal("Button bReload", loaded.Bindings["Y"]);
        Assert.Contains(loaded.Resolve("T"), a => a.Name == "Fire");
    }

    [Theory]
    [InlineData("Linear", 1f)]
    [InlineData("CurveSoft", 2f)]
    [InlineData("CurveMedium", 2.5f)]
    [InlineData("CurveAggressive", 3f)]
    public void MouseResponseMigrationPreservesGainAndIsIdempotent(string response, float gain)
    {
        var source = new GameSettings();
        source.SpyKbm.FreeLook = 10;
        source.SpyKbm.LookResponse = response;
        GameSettings loaded = GameSettings.FromJson(source.ToJson());
        Assert.Equal(10 * gain, loaded.SpyKbm.FreeLook);
        Assert.Equal("Linear", loaded.SpyKbm.LookResponse);
        Assert.Equal("CurveAggressive", loaded.SpyPad.LookResponse);
        loaded.Clamp();
        Assert.Equal(10 * gain, GameSettings.FromJson(loaded.ToJson()).SpyKbm.FreeLook);
    }
}
