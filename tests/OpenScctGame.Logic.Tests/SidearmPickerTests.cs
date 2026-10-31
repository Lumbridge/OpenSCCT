using OpenScct.Core.Engine;
using OpenScctGame.Options;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class SidearmPickerTests
{
    [Fact]
    public void ChoosingAnotherPistolMovesTheDisplacedGadgetToItsOldSlot()
    {
        var saved = new LoadoutSettings();
        saved.MercGadgets[0] = Sidearms.All[0].CarryId;
        string frag = saved.MercGadgets[1];
        var model = new GearMenuModel(false, 4, [frag], 255, [], saved, false, 55);
        model.Choose(1, Sidearms.All[3].CarryId);
        Assert.Equal(frag, model.Gadgets[0]);
        Assert.Equal(Sidearms.All[3].CarryId, model.Gadgets[1]);
    }

    [Fact]
    public void PrestigeKeepsTheSavedLockedChoiceAndPreventsSelectingItAgain()
    {
        var saved = new LoadoutSettings();
        saved.MercGadgets[0] = Sidearms.All[3].CarryId;
        var model = new GearMenuModel(false, 4, [], 0, [], saved, false, 1);
        Assert.False(model.IsAllowed(Sidearms.All[3].CarryId));
        Assert.Equal(Sidearms.All[3].CarryId, model.Gadgets[0]);
        model.Choose(1, Sidearms.All[3].CarryId);
        Assert.NotEqual(Sidearms.All[3].CarryId, model.Gadgets[1]);
        model.SaveTo(saved);
        Assert.Equal(Sidearms.All[3].CarryId, saved.MercGadgets[0]);
        var offline = new GearMenuModel(false, 4, [], 0, [], saved, false, 1, true);
        Assert.True(offline.IsAllowed(Sidearms.All[3].CarryId));
        var spy = new GearMenuModel(true, 4, [], 255, [], saved, false, 55, true);
        Assert.False(spy.IsAllowed(Sidearms.All[0].CarryId));
    }
}
