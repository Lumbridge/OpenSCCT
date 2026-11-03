using OpenScctGame.Options;
using System.Linq;
using OpenScct.Game.Equipment;
using OpenScct.Server.Online;
using OpenScctGame.Controls;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class CloakSettingsTests
{
    [Fact]
    public void SavedCloakSlotBecomesARealGadgetAndIsAbsentFromPicker()
    {
        var settings = new GameSettings();
        settings.Loadout.SpyGadgets[0] = SpyCloak.LegacyGadgetClass;
        settings.Clamp();
        Assert.DoesNotContain(settings.Loadout.SpyGadgets, SpyCloak.IsLegacyGadget);
        Assert.Equal(4, settings.Loadout.SpyGadgets.Distinct().Count());
        var model = GearMenuModel.ForLobby(0, new LobbySetup(), settings.Loadout)!;
        Assert.DoesNotContain(model.Options, SpyCloak.IsLegacyGadget);
        Assert.False(model.IsAllowed(SpyCloak.LegacyGadgetClass));
        Assert.Equal(7, model.Options.Count);
    }

    [Fact]
    public void OldCloakRestrictionBitDoesNotCountAsAnAvailableGadget()
    {
        var rules = new AlternateRules { SpyGadgets = 8, SpyGadgetCount = 4 };
        Assert.False(rules.GadgetAllowed(0, SpyCloak.LegacyGadgetOption));
        Assert.Equal(0, rules.Clamped().SpyGadgetCount);
        Assert.Equal(7, (rules with { SpyGadgets = 255 }).AvailableSpyGadgets);
    }

    [Fact]
    public void KeyboardDefaultAndMigrationPreserveCustomBindingsAndDeliberateUnbinding()
    {
        var defaults = DefaultKeyLayout.ApplyProfile(DefaultKeyLayout.Create(), null, "Gadget");
        Assert.Contains(defaults.Resolve("K"), a => a.Name == "bCloak");
        Assert.DoesNotContain(defaults.Resolve("RightMouse"), a => a.Name == "bCloak");
        var custom = DefaultKeyLayout.ApplyProfile(DefaultKeyLayout.Create(), "[Engine.Input]\nIK_K=CustomAction\nIK_J=Button bCloak", "Gadget");
        Assert.Single(custom.Resolve("K"), a => a.Name == "CustomAction");
        Assert.Single(custom.Resolve("J"), a => a.Name == "bCloak");
        defaults.Bind("K", "");
        var reloaded = DefaultKeyLayout.ApplyProfile(DefaultKeyLayout.Create(), defaults.ToIni(), "Gadget");
        Assert.DoesNotContain(reloaded.Resolve("K"), a => a.Name == "bCloak");
    }

    [Fact]
    public void ControllerDefaultMigratesMissingChordAndHonorsClearedChord()
    {
        var settings = new GameSettings();
        settings.Pad.SpyButtons.Remove("LB+RB");
        settings.Clamp();
        Assert.Equal("Action:cloak", settings.Pad.SpyButtons["LB+RB"]);
        settings.Pad.SpyButtons["LB+RB"] = "";
        settings.Clamp();
        Assert.Equal("", settings.Pad.SpyButtons["LB+RB"]);
        Assert.Contains(ControlActions.ForRole(false), a => a.Id == "cloak");
        Assert.DoesNotContain(ControlActions.ForRole(true), a => a.Id == "cloak");
    }

    [Fact]
    public void CloakDefaultsToToggleAndHoldSurvivesSettingsRoundTrip()
    {
        var settings = new GameSettings();
        Assert.Equal("Toggle", settings.SpyKbm.CloakBehavior);
        settings.SpyKbm.CloakBehavior = "Hold";
        var loaded = GameSettings.FromJson(settings.ToJson());
        Assert.Equal("Hold", loaded.SpyKbm.CloakBehavior);
        loaded.SpyKbm.CloakBehavior = "invalid";
        loaded.Clamp();
        Assert.Equal("Toggle", loaded.SpyKbm.CloakBehavior);
    }
}
