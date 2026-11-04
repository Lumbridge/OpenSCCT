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
        Assert.Contains(draft.Layout.Resolve("RightMouse"), a => a.Name == "AltFire");
        Assert.StartsWith("AltFire |", draft.CommandOf("RightMouse"));
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
    public void AddAlternateDeduplicatesAndPreservesEveryOtherInputAndUntouchedCommand()
    {
        var draft = new ActionBindingDraft(KeyLayout.Parse(Ini));
        string untouched = draft.CommandOf("RightMouse");
        draft.Add(Action("reload"), "X");
        draft.Add(Action("reload"), "Ctrl+R");
        draft.Add(Action("reload"), "Y");
        string saved = draft.Layout.ToIni();
        draft.Add(Action("reload"), "x");
        Assert.Equal(saved, draft.Layout.ToIni());
        Assert.Equal(new[] { "R", "X", "Ctrl+R", "Y" }, draft.Keys(Action("reload")));
        Assert.Equal(untouched, draft.CommandOf("RightMouse"));
        Assert.Equal(saved, KeyLayout.Parse(saved).ToIni());
    }

    [Fact]
    public void ReplaceAndRemoveASelectedAlternateLeaveOtherInputsAndSharedAliasesIntact()
    {
        var draft = new ActionBindingDraft(KeyLayout.Parse(Ini));
        draft.Add(Action("breath"), "X");
        draft.Add(Action("breath"), "Y");
        draft.Replace(Action("breath"), "RightMouse", "Ctrl+E");
        Assert.Equal(new[] { "Ctrl+E", "X", "Y" }, draft.Keys(Action("breath")));
        Assert.Equal("AltFire | GadgetQuickChoice 1 | CustomThing mode=keep", draft.CommandOf("RightMouse"));
        draft.Remove(Action("breath"), "X");
        Assert.Equal(new[] { "Ctrl+E", "Y" }, draft.Keys(Action("breath")));
        Assert.Equal(KeyLayout.Parse(Ini).Aliases, draft.Layout.Aliases);
    }

    [Fact]
    public void ShareAndReplaceDestinationOnlyAffectTheChosenSourceAndDestination()
    {
        var draft = new ActionBindingDraft(KeyLayout.Parse(Ini));
        draft.Add(Action("breath"), "X");
        draft.Replace(Action("breath"), "X", "R");
        Assert.Equal("Button bReload | Button bHoldBreath", draft.CommandOf("R"));
        Assert.Empty(draft.CommandOf("X"));
        Assert.Contains("RightMouse", draft.Keys(Action("breath")));

        draft.Replace(Action("breath"), "R", "RightMouse", replaceDestination: true);
        Assert.Equal("Button bHoldBreath", draft.CommandOf("RightMouse"));
        Assert.Equal("Button bReload", draft.CommandOf("R"));
        Assert.Equal(new[] { "RightMouse" }, draft.Keys(Action("breath")));
    }

    [Fact]
    public void ScopedSwapPreservesOtherAlternatesAndMovesCompleteCustomDestination()
    {
        var draft = new ActionBindingDraft(KeyLayout.Parse(Ini));
        draft.Add(Action("reload"), "Y");
        draft.Add(Action("reload"), "X");
        draft.Swap(Action("reload"), "R", "RightMouse");
        Assert.Equal(new[] { "RightMouse", "Y", "X" }, draft.Keys(Action("reload")));
        Assert.Equal("Button bReload", draft.CommandOf("RightMouse"));
        Assert.Equal("AltFire | GadgetQuickChoice 1 | HoldBreath | CustomThing mode=keep", draft.CommandOf("R"));
        var loaded = new ActionBindingDraft(KeyLayout.Parse(draft.Layout.ToIni()));
        Assert.Equal(draft.Layout.ToIni(), loaded.Layout.ToIni());
        Assert.Contains("R", loaded.Keys(Action("breath")));
    }

    [Fact]
    public void ScopedSwapRetainsUnrelatedSourceActionsAndDestinationAliasText()
    {
        const string source = """
            [Engine.Input]
            Aliases[0]=(Command="CustomThing mode=keep",Alias="CustomDestination")
            IK_X=Button bReload | Button bHoldBreath
            IK_Y=CustomDestination
            IK_Z=Button bReload
            """;
        var draft = new ActionBindingDraft(KeyLayout.Parse(source));
        draft.Swap(Action("reload"), "X", "Y");
        Assert.Equal("Button bHoldBreath | CustomDestination", draft.CommandOf("X"));
        Assert.Equal("Button bReload", draft.CommandOf("Y"));
        Assert.Equal("Button bReload", draft.CommandOf("Z"));
        Assert.Equal(KeyLayout.Parse(source).Aliases, draft.Layout.Aliases);
    }

    [Fact]
    public void AddingAnEarlierInputKeepsThePrimaryAndSelectedReplacementKeepsItsSlot()
    {
        var forward = Action("forward");
        var draft = new ActionBindingDraft(KeyLayout.Parse("[Engine.Input]\nIK_W=Button BT_Up"));
        draft.Add(forward, "A");
        Assert.Equal(new[] { "W", "A" }, draft.Keys(forward));
        draft.Replace(forward, "A", "Z");
        Assert.Equal(new[] { "W", "Z" }, draft.Keys(forward));
        draft.Replace(forward, "W", "Y");
        Assert.Equal(new[] { "Y", "Z" }, draft.Keys(forward));
    }

    [Fact]
    public void SharingAnOccupiedEarlierInputAppendsWithoutChangingEitherActionsPrimary()
    {
        const string source = """
            [Engine.Input]
            IK_A=Button bReload | CustomThing mode=keep
            IK_W=Button BT_Up
            """;
        var draft = new ActionBindingDraft(KeyLayout.Parse(source));
        draft.Add(Action("reload"), "R");
        draft.Add(Action("forward"), "A");
        Assert.Equal(new[] { "W", "A" }, draft.Keys(Action("forward")));
        Assert.Equal(new[] { "A", "R" }, draft.Keys(Action("reload")));
        Assert.Equal("Button bReload | CustomThing mode=keep | Button BT_Up", draft.CommandOf("A"));
        var loaded = new ActionBindingDraft(KeyLayout.Parse(draft.Layout.ToIni()));
        Assert.Equal(new[] { "W", "A" }, loaded.Keys(Action("forward")));
        Assert.Equal(new[] { "A", "R" }, loaded.Keys(Action("reload")));
    }

    [Fact]
    public void MoreThanTwoAlternatesKeepInsertionOrderAndRemovingInputsCompactsThatOrder()
    {
        var forward = Action("forward");
        var draft = new ActionBindingDraft(KeyLayout.Parse("[Engine.Input]\nIK_W=Button BT_Up"));
        foreach (string input in new[] { "Z", "A", "Ctrl+Q", "G" }) draft.Add(forward, input);
        Assert.Equal(new[] { "W", "Z", "A", "Ctrl+Q", "G" }, draft.Keys(forward));
        string beforeDuplicate = draft.Layout.ToIni();
        draft.Add(forward, "a");
        Assert.Equal(beforeDuplicate, draft.Layout.ToIni());
        draft.Remove(forward, "A");
        Assert.Equal(new[] { "W", "Z", "Ctrl+Q", "G" }, draft.Keys(forward));
        draft.Remove(forward, "W");
        Assert.Equal(new[] { "Z", "Ctrl+Q", "G" }, draft.Keys(forward));
        draft.Add(forward, "B");
        Assert.Equal(new[] { "Z", "Ctrl+Q", "G", "B" }, draft.Keys(forward));
        var loaded = new ActionBindingDraft(KeyLayout.Parse(draft.Layout.ToIni()));
        Assert.Equal(new[] { "Z", "Ctrl+Q", "G", "B" }, loaded.Keys(forward));
    }

    [Fact]
    public void SwappingASelectedAlternateOrPrimaryRetainsItsPositionAndAllOtherInputs()
    {
        const string source = """
            [Engine.Input]
            IK_A=Button bReload | CustomDestination keep=1
            IK_W=Button BT_Up | CustomSource keep=2
            IK_X=Button bCrouch
            """;
        var forward = Action("forward");
        var draft = new ActionBindingDraft(KeyLayout.Parse(source));
        draft.Add(forward, "Z");
        draft.Add(forward, "B");
        draft.Swap(forward, "Z", "A");
        Assert.Equal(new[] { "W", "A", "B" }, draft.Keys(forward));
        Assert.Equal("Button bReload | CustomDestination keep=1", draft.CommandOf("Z"));
        Assert.Equal("Button BT_Up | CustomSource keep=2", draft.CommandOf("W"));
        draft.Swap(forward, "W", "X");
        Assert.Equal(new[] { "X", "A", "B" }, draft.Keys(forward));
        Assert.Equal("CustomSource keep=2 | Button bCrouch", draft.CommandOf("W"));
        var loaded = new ActionBindingDraft(KeyLayout.Parse(draft.Layout.ToIni()));
        Assert.Equal(new[] { "X", "A", "B" }, loaded.Keys(forward));
    }

    [Fact]
    public void PrimaryAndAlternateOrderSurviveSaveParseCloneAndANewDraft()
    {
        var forward = Action("forward");
        var draft = new ActionBindingDraft(KeyLayout.Parse("[Engine.Input]\nIK_W=Button BT_Up"));
        draft.Add(forward, "A");
        draft.Add(forward, "Ctrl+E");
        draft.Replace(forward, "W", "Z");
        Assert.Equal(new[] { "Z", "A", "Ctrl+E" }, draft.Keys(forward));
        string saved = draft.Layout.ToIni();
        var loaded = new ActionBindingDraft(KeyLayout.Parse(saved).Clone());
        Assert.Equal(new[] { "Z", "A", "Ctrl+E" }, loaded.Keys(forward));
        Assert.Equal(saved, loaded.Layout.ToIni());
        Assert.False(loaded.Dirty);
        loaded.Add(forward, "B");
        Assert.Equal(new[] { "Z", "A", "Ctrl+E", "B" }, loaded.Keys(forward));
        Assert.True(loaded.Dirty);
    }

    [Fact]
    public void ReadingAnUnorderedLegacyLayoutKeepsItsExistingOrderAndDoesNotCreateAnEdit()
    {
        const string source = """
            [Engine.Input]
            IK_W=Button BT_Up
            IK_Z=Button BT_Up
            IK_A=Button BT_Up
            """;
        var draft = new ActionBindingDraft(KeyLayout.Parse(source));
        string before = draft.Layout.ToIni();
        Assert.Equal(new[] { "A", "W", "Z" }, draft.Keys(Action("forward")));
        Assert.Equal(before, draft.Layout.ToIni());
        Assert.False(draft.Dirty);
        var loaded = new ActionBindingDraft(KeyLayout.Parse(before));
        Assert.Equal(new[] { "A", "W", "Z" }, loaded.Keys(Action("forward")));
        Assert.False(loaded.Dirty);
    }

    [Fact]
    public void RemovingAnActionSplitsOnlyItsMixedAliasAndPreservesOtherAliasInvocations()
    {
        const string source = """
            [Engine.Input]
            Aliases[0]=(Command="Button bReload | CustomThing mode=keep",Alias="Mixed")
            Aliases[1]=(Command="OtherThing precise=1",Alias="Unrelated")
            IK_X=Mixed | Unrelated
            IK_Y=Mixed
            """;
        var draft = new ActionBindingDraft(KeyLayout.Parse(source));
        draft.Remove(Action("reload"), "X");
        Assert.Equal("CustomThing mode=keep | Unrelated", draft.CommandOf("X"));
        Assert.Equal("Mixed", draft.CommandOf("Y"));
        Assert.Equal(KeyLayout.Parse(source).Aliases, draft.Layout.Aliases);
    }

    [Fact]
    public void ReplacingOrSwappingWithTheSameInputLeavesSharedCommandsUnchanged()
    {
        var draft = new ActionBindingDraft(KeyLayout.Parse(Ini));
        string before = draft.Layout.ToIni();
        draft.Replace(Action("breath"), "RightMouse", "rightmouse", replaceDestination: true);
        draft.Swap(Action("breath"), "RightMouse", "rightmouse");
        Assert.Equal(before, draft.Layout.ToIni());
        Assert.False(draft.Dirty);
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
        Assert.Contains(ControlActions.ForRole(false), a => a.Id == "cloak" && a.Label.Contains("Cloak"));
        Assert.DoesNotContain(ControlActions.ForRole(false), a => a.Id == "breath");
        Assert.DoesNotContain(ControlActions.ForRole(true), a => a.Id == "roll");
    }

    [Theory]
    [InlineData(false, "F", "Switch fire mode")]
    [InlineData(true, "Z", "Back to wall")]
    [InlineData(true, "V", "Previous sticky camera")]
    [InlineData(false, "F3", "Controls reference (unavailable)")]
    [InlineData(true, "F4", "HUD help (unavailable)")]
    [InlineData(false, "F11", "Quit shortcut (unavailable)")]
    [InlineData(false, "F1", "Night vision")]
    [InlineData(true, "F1", "Motion tracking vision")]
    [InlineData(false, "B", "Binoculars")]
    [InlineData(true, "B", "Sniper scope")]
    public void BindingDescriptionIncludesHiddenActionsAndUsesCurrentRoleNames(bool merc, string key, string expected)
    {
        var draft = new ActionBindingDraft(DefaultKeyLayout.Create());
        Assert.Equal(expected, draft.Describe(key, merc));
        Assert.False(draft.Dirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CombinedBindingDescriptionKeepsBothRolesAndCustomCommandsWithoutRepeatingActions(bool merc)
    {
        var draft = new ActionBindingDraft(KeyLayout.Parse(Ini));
        Assert.Equal("Use selected gadget / alternate action / Quick-use gadget slot 2 / Hold breath / CustomThing mode=keep",
            draft.Describe("RightMouse", merc));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryStoredDefaultBindingHasADescription(bool merc)
    {
        var draft = new ActionBindingDraft(DefaultKeyLayout.Create());
        foreach (string key in draft.Layout.Bindings.Keys)
            Assert.False(string.IsNullOrWhiteSpace(draft.Describe(key, merc)), key + " has no binding description");
        Assert.Empty(draft.Describe("UnboundKey", merc));
    }

    [Fact]
    public void BindingDescriptionFallsBackToSavedCommandForAnEmptyAlias()
    {
        var draft = new ActionBindingDraft(KeyLayout.Parse("[Engine.Input]\nAliases[0]=(Command=\"\",Alias=\"EmptyAlias\")\nIK_X=EmptyAlias"));
        Assert.Equal("EmptyAlias", draft.Describe("X", false));
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
