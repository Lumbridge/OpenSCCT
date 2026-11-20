using System.Linq;
using OpenScct.Game.Equipment;
using OpenScct.Server.Online;
using OpenScctGame.Options;
using OpenScctGame.UI;
using Xunit;

namespace OpenScctGame.Logic.Tests;

public sealed class RifleUnlockPickerTests
{
    [Fact]
    public void DisabledRiflesAreHiddenEvenForUnrestrictedPlayersAndSavedChoicesFallBack()
    {
        var saved = new LoadoutSettings { MercWeapon = "Svm.M4A1S" };
        var model = GearMenuModel.ForLobby(1, new LobbySetup { Rifles = false }, saved, 55, true)!;
        Assert.Equal(new[] { MercPrimarySelection.Smg, MercPrimarySelection.Shotgun }, model.Weapons);
        Assert.Equal(MercPrimarySelection.Smg, model.ChosenWeapon);
        model.ChooseWeapon("Svm.M4A1S");
        Assert.Equal(MercPrimarySelection.Smg, model.ChosenWeapon);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(19, 2)]
    [InlineData(20, 3)]
    [InlineData(29, 3)]
    [InlineData(30, 4)]
    public void RiflesShowInUnlockOrderAndOnlyUnlockedOnesCanBePickedOrCycled(int level, int count)
    {
        var saved = new LoadoutSettings { MercWeapon = "Svm.M4A1S" };
        var model = GearMenuModel.ForLobby(1, new LobbySetup { Rifles = true }, saved, level)!;
        var rifles = model.Weapons.Where(MercPrimarySelection.RequiresRifleOption).ToArray();
        Assert.Equal(new[] { "Svm.Galil", "Svm.BoltRifle", "Svm.DesignatedMarksman", "Svm.Famas", "Svm.AK47", "Svm.M4A1S" }, rifles);
        Assert.Equal(count + 2, rifles.Count(model.IsAllowed));
        Assert.Equal(level == 30 ? "Svm.M4A1S" : MercPrimarySelection.Smg, model.ChosenWeapon);
        foreach (string rifle in rifles)
        {
            string before = model.ChosenWeapon;
            model.ChooseWeapon(rifle);
            Assert.Equal(model.IsAllowed(rifle) ? rifle : before, model.ChosenWeapon);
        }
        for (int i = 0; i < 12; i++)
        {
            model.CycleWeapon(1);
            Assert.True(model.IsAllowed(model.ChosenWeapon));
        }
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(4, false, false)]
    [InlineData(5, true, false)]
    [InlineData(9, true, false)]
    [InlineData(10, true, true)]
    public void LockedGadgetsStayVisibleShowTheirLevelAndCannotBeEquipped(int level, bool hack, bool clone)
    {
        var saved = new LoadoutSettings();
        saved.SpyGadgets[0] = "Svm.MineHackGadget";
        saved.SpyGadgets[1] = "Svm.CloneGadget";
        var model = GearMenuModel.ForLobby(0, new LobbySetup(), saved, level)!;
        Assert.Contains("Svm.MineHackGadget", model.Options);
        Assert.Contains("Svm.CloneGadget", model.Options);
        Assert.Equal(hack, model.IsAllowed("Svm.MineHackGadget"));
        Assert.Equal(clone, model.IsAllowed("Svm.CloneGadget"));
        Assert.Equal(5, model.RequiredLevel("Svm.MineHackGadget"));
        Assert.Equal(10, model.RequiredLevel("Svm.CloneGadget"));
        if (!clone)
        {
            Assert.Equal("Requires level 10.", model.Restriction("Svm.CloneGadget"));
            Assert.DoesNotContain("Svm.CloneGadget", model.Gadgets);
            string before = model.Gadgets[0];
            model.Choose(0, "Svm.CloneGadget");
            Assert.Equal(before, model.Gadgets[0]);
        }
    }

    [Fact]
    public void PracticeUnlocksGearButStillRequiresTheRifleHostOption()
    {
        var setup = new LobbySetup { Practice = true, Rifles = false };
        Assert.DoesNotContain(GearMenuModel.ForLobby(1, setup, new(), 1)!.Weapons, MercPrimarySelection.RequiresRifleOption);
        var merc = GearMenuModel.ForLobby(1, setup with { Rifles = true }, new(), 1)!;
        Assert.All(merc.Weapons, w => Assert.True(merc.IsAllowed(w)));
        var spy = GearMenuModel.ForLobby(0, setup, new(), 1)!;
        Assert.True(spy.IsAllowed("Svm.CloneGadget"));
    }

    [Theory]
    [InlineData("SBase.SWeaponDefense", WeaponAttachments.M4, MercPrimarySelection.Smg)]
    [InlineData("sbase.sweapondefense_uzi", MercPrimarySelection.Smg, MercPrimarySelection.Smg)]
    [InlineData("SBase.SWeaponDefense_Shotgun", MercPrimarySelection.Shotgun, MercPrimarySelection.Shotgun)]
    public void LegacyPreferencesFollowTheirReplacementAcrossRooms(string previous, string unrestricted, string stock)
    {
        var profile = new LoadoutSettings { MercWeapon = previous };
        var stockRoom = GearMenuModel.ForLobby(1, new LobbySetup { Rifles = false }, profile, 1)!;
        Assert.Equal(stock, stockRoom.ChosenWeapon);
        stockRoom.SaveTo(profile);
        var openRoom = GearMenuModel.ForLobby(1, new LobbySetup { Rifles = true }, profile, 30)!;
        Assert.Equal(unrestricted, openRoom.ChosenWeapon);
        Assert.DoesNotContain(openRoom.Weapons, MercPrimarySelection.IsLegacy);
    }
}
