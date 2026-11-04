using OpenScctGame.Options;
using System.Collections.Generic;
using Xunit;

namespace OpenScctGame.Logic.Tests;

/// <summary>The action-first editor changes individual gesture entries in the existing controller dictionaries.</summary>
public sealed class ControllerBindingEditingTests
{
    [Fact]
    public void AlternateAssignmentsAndExplicitRemovalRoundTripWithoutFlatteningContextualPresets()
    {
        var settings = new GameSettings();
        settings.Pad.SpyButtons["X"] = "Action:jump";
        GameSettings alternate = settings.Clone();
        Assert.Equal("Action:jump", alternate.Pad.SpyButtons["Y"]);
        Assert.Equal("Action:jump", alternate.Pad.SpyButtons["X"]);
        Assert.Equal(PadSettings.SpyFireAction, alternate.Pad.SpyButtons["RT"]);
        Assert.Equal(PadSettings.SpyGadgetAction, alternate.Pad.SpyButtons["LT"]);

        alternate.Pad.SpyButtons["X"] = "";
        GameSettings removed = alternate.Clone();
        Assert.Equal("", removed.Pad.SpyButtons["X"]);
        Assert.Equal("Action:jump", removed.Pad.SpyButtons["Y"]);
        Assert.Equal(PadSettings.SpyFireAction, removed.Pad.SpyButtons["RT"]);
        Assert.Equal(PadSettings.MercGadgetAction, removed.Pad.MercButtons["LT"]);
    }

    [Fact]
    public void IndividualEditsPreserveLegacyLinkedAndCustomAssignmentsAndTheOtherRole()
    {
        var settings = new GameSettings();
        settings.Pad.SpyLayoutVersion = 0;
        settings.Pad.SpyButtons["X"] = "CustomSavedAction";
        settings.Pad.SpyButtons["A"] = "Key:O";
        string otherRole = settings.ToJson();
        settings.Pad.SpyButtons["Y"] = "Action:jump";
        GameSettings loaded = settings.Clone();
        Assert.Equal(0, loaded.Pad.SpyLayoutVersion);
        Assert.Equal("CustomSavedAction", loaded.Pad.SpyButtons["X"]);
        Assert.Equal("Key:O", loaded.Pad.SpyButtons["A"]);
        Assert.Equal("Action:jump", loaded.Pad.SpyButtons["Y"]);
        Assert.Equal(GameSettings.FromJson(otherRole).Pad.MercButtons, loaded.Pad.MercButtons);
        Assert.Equal(settings.Pad.MercLayoutVersion, loaded.Pad.MercLayoutVersion);
    }

    [Fact]
    public void ReadingOlderControllerProfilesDoesNotCreateOrderMetadataOrDirtySettings()
    {
        var settings = GameSettings.FromJson("{}");
        string before = settings.ToJson();
        Assert.Equal(new[] { "RT" }, PadBindingOrder.Order(settings.Pad, false, "fire", new[] { "RT" }));
        Assert.Empty(settings.Pad.SpyBindingOrder);
        Assert.Equal(before, settings.ToJson());
    }

    [Fact]
    public void AddingAnEarlierPhysicalControlDoesNotMoveThePrimaryContextualBinding()
    {
        var settings = new GameSettings();
        // RT remains the primary fire input even when a direct A assignment would otherwise sort before it.
        settings.Pad.SpyButtons["A"] = "Action:fire";
        PadBindingOrder.RememberOrder(settings.Pad, false, "fire", new[] { "RT", "A" });
        Assert.Equal(new[] { "RT", "A" }, PadBindingOrder.Order(settings.Pad, false, "fire", new[] { "A", "RT" }));
        GameSettings loaded = settings.Clone();
        Assert.Equal(new[] { "RT", "A" }, PadBindingOrder.Order(loaded.Pad, false, "fire", new[] { "A", "RT" }));
        Assert.Equal(PadSettings.SpyFireAction, loaded.Pad.SpyButtons["RT"]);
        Assert.Equal("Action:fire", loaded.Pad.SpyButtons["A"]);
    }

    [Fact]
    public void ReplacingASelectedControllerInputKeepsItsPositionAndClearCompactsRemainingInputs()
    {
        var settings = new GameSettings();
        PadBindingOrder.RememberOrder(settings.Pad, false, "forward", new[] { "Start", "A", "X" });
        // Replacing alternate A with RT must not make RT primary or disturb the third input X.
        PadBindingOrder.RememberOrder(settings.Pad, false, "forward", new[] { "Start", "RT", "X" });
        var loaded = settings.Clone();
        Assert.Equal(new[] { "Start", "RT", "X" }, PadBindingOrder.Order(loaded.Pad, false, "forward", new[] { "X", "RT", "Start" }));
        Assert.Equal(new[] { "RT", "X" }, PadBindingOrder.Order(loaded.Pad, false, "forward", new[] { "X", "RT" }));
        Assert.Equal(new[] { "Start", "RT", "X", "Y" }, PadBindingOrder.Order(loaded.Pad, false, "forward", new[] { "Y", "X", "RT", "Start" }));
    }

    [Fact]
    public void ControllerOrderingIsPerRoleAndResetOnlyClearsTheChosenRole()
    {
        var settings = new GameSettings();
        PadBindingOrder.RememberOrder(settings.Pad, false, "forward", new[] { "Start", "A" });
        PadBindingOrder.RememberOrder(settings.Pad, true, "forward", new[] { "X", "B" });
        settings.Pad.ResetButtons(false);
        Assert.Empty(settings.Pad.SpyBindingOrder);
        Assert.Equal(new[] { "X", "B" }, PadBindingOrder.Order(settings.Pad, true, "forward", new[] { "B", "X" }));
    }

    [Fact]
    public void InvalidOrderMetadataDoesNotAddRuntimeBindingsAndValidOrderSurvivesNormalization()
    {
        var settings = new GameSettings();
        var buttons = new Dictionary<string, string>(settings.Pad.SpyButtons);
        settings.Pad.SpyBindingOrder["fire"] = new() { "RT", "RT", "Unsupported", "A" };
        settings.Pad.SpyBindingOrder["saved_CustomAction"] = new() { "Start", "X" };
        var loaded = settings.Clone();
        Assert.Equal(new[] { "RT", "A" }, loaded.Pad.SpyBindingOrder["fire"]);
        Assert.Equal(new[] { "Start", "X" }, loaded.Pad.SpyBindingOrder["saved_CustomAction"]);
        Assert.Equal(buttons, loaded.Pad.SpyButtons);
    }
}
