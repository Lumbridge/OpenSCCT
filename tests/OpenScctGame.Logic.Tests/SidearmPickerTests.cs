using OpenScct.Core.Engine;
using OpenScctGame.Options;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class SidearmPickerTests
{
    [Fact]
    public void SessionWithoutSidearmsDisablesPickerAndPreservesSavedPistol()
    {
        var saved = new LoadoutSettings { MercSidearm = Sidearms.Get(SidearmKind.Magnum44).CarryId };
        var model = GearMenuModel.ForLobby(1, new OpenScct.Server.Online.LobbySetup(), saved, 55)!;
        Assert.False(model.SidearmsEnabled);
        Assert.Empty(model.SidearmOptions);
        Assert.False(model.IsAllowed(saved.MercSidearm));
        model.SaveTo(saved);
        Assert.Equal(Sidearms.Get(SidearmKind.Magnum44).CarryId, saved.MercSidearm);
        var enabled = GearMenuModel.ForLobby(1, new OpenScct.Server.Online.LobbySetup { Sidearms = true }, saved, 55)!;
        Assert.True(enabled.SidearmsEnabled);
        Assert.NotEmpty(enabled.SidearmOptions);
    }

    [Fact]
    public void SavedLegacyPistolMigratesEvenOutsideTheHostsGadgetCount()
    {
        var saved = new LoadoutSettings();
        saved.MercGadgets[3] = Sidearms.Get(SidearmKind.Automatic).CarryId;
        var model = new GearMenuModel(false, 0, [], 0, [], saved, false, 55);
        model.ChooseSidearm(Sidearms.Get(SidearmKind.Magnum44).CarryId);
        model.SaveTo(saved);
        saved.Clamp();
        Assert.Equal(Sidearms.Get(SidearmKind.Magnum44).CarryId, saved.MercSidearm);
        Assert.All(saved.MercGadgets, g => Assert.Null(Sidearms.Find(g)));
    }

    [Fact]
    public void PistolsAppearInUnlockOrderAndOnlyUnsuppressedIsAvailableAtLevelOne()
    {
        var model = new GearMenuModel(false, 4, [], 255, [], new LoadoutSettings(), false, 1);
        Assert.Equal(new[] { "unsuppressed", "automatic", "suppressed", "magnum44", "desert_eagle" },
            System.Linq.Enumerable.Select(model.SidearmOptions, id => Sidearms.Find(id)!.Id));
        Assert.Empty(model.Options);
        foreach (string option in model.SidearmOptions)
            Assert.Equal(Sidearms.Find(option)!.Kind == SidearmKind.Unsuppressed, model.IsAllowed(option));
    }

    [Fact]
    public void ChoosingAnotherPistolPreservesAllGadgetSlotsAndMigratesOldChoices()
    {
        var saved = new LoadoutSettings();
        saved.MercGadgets[0] = Sidearms.All[0].CarryId;
        string frag = saved.MercGadgets[1];
        var model = new GearMenuModel(false, 4, [frag], 255, [], saved, false, 55);
        Assert.Equal(Sidearms.All[0].CarryId, model.ChosenSidearm);
        string[] gadgets = (string[])model.Gadgets.Clone();
        model.ChooseSidearm(Sidearms.All[3].CarryId);
        Assert.Equal(gadgets, model.Gadgets);
        Assert.Equal(Sidearms.All[3].CarryId, model.ChosenSidearm);
        model.Choose(1, Sidearms.All[3].CarryId);
        Assert.Equal(gadgets, model.Gadgets);
        model.SaveTo(saved);
        Assert.Equal(model.ChosenSidearm, saved.MercSidearm);
    }

    [Fact]
    public void PrestigeClearsTheLockedChoiceAndPreventsSelectingItAgain()
    {
        var saved = new LoadoutSettings();
        saved.MercGadgets[0] = Sidearms.All[3].CarryId;
        var model = new GearMenuModel(false, 4, [], 0, [], saved, false, 1);
        Assert.False(model.IsAllowed(Sidearms.All[3].CarryId));
        Assert.Equal(LoadoutSettings.DefaultSidearm, model.ChosenSidearm);
        model.ChooseSidearm(Sidearms.All[3].CarryId);
        Assert.Equal(LoadoutSettings.DefaultSidearm, model.ChosenSidearm);
        model.SaveTo(saved);
        Assert.Equal(LoadoutSettings.DefaultSidearm, saved.MercSidearm);
        saved.MercGadgets[0] = Sidearms.All[3].CarryId;
        var offline = new GearMenuModel(false, 4, [], 0, [], saved, false, 1, true);
        Assert.True(offline.IsAllowed(Sidearms.All[3].CarryId));
        Assert.Equal(Sidearms.All[3].CarryId, offline.ChosenSidearm);
        var spy = new GearMenuModel(true, 4, [], 255, [], saved, false, 55, true);
        Assert.False(spy.IsAllowed(Sidearms.All[0].CarryId));
    }
}
