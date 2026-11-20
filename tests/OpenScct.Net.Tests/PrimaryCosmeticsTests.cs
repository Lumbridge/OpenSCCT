using System;
using System.Linq;
using OpenScct.Core.Engine;
using OpenScct.Net.Cosmetics;
using Xunit;

namespace OpenScct.Net.Tests;

public sealed class PrimaryCosmeticsTests
{
    [Fact]
    public void ExistingRifleAndPistolBytesRetainTheirIdsAndEntitlements()
    {
        // A saved pre-mastery look with the shared golden rifle and golden unsuppressed pistol.
        var saved = CosmeticLook.ParseHex("ea0000000000003c000000");
        Assert.Equal("merc.ar.golden", saved[CosmeticSlot.GunAr]?.Id);
        Assert.Equal("merc.unsuppressed.golden", saved[CosmeticSlot.Unsuppressed]?.Id);
        var oldTicketBits = new byte[30];
        oldTicketBits[7] = 8; // bit 59
        oldTicketBits[29] = 2; // bit 233
        var entitlements = new CosmeticUnlocks(oldTicketBits);
        Assert.Equal(saved, saved.Validate(CosmeticSide.Merc, entitlements));
        Assert.Equal("merc.ar.golden", saved.WeaponItem("Svm.M4A1S")?.Id);
        Assert.False(entitlements.Has(CosmeticCatalog.Find("merc.m4a1s.golden")!));
    }

    [Fact]
    public void FourApprovedRiflesStoreIndependentFinishesAlongsideAllOldSlots()
    {
        string[] equipped = ["merc.ar.pyre", "merc.m4a1s.woodland", "merc.ak47.blue_tiger", "merc.galil.desert",
            "merc.famas.golden", "merc.unsuppressed.cryo"];
        var look = CosmeticLook.FromIds(CosmeticSide.Merc, equipped);
        var ticket = new CosmeticUnlocks(CosmeticUnlocks.Of(equipped).ToBytes());
        var received = CosmeticLook.ParseHex(look.ToHex()).Validate(CosmeticSide.Merc, ticket);
        Assert.Equal(look, received);
        Assert.Equal(equipped[0], received.WeaponItem("SBase.SWeaponDefense")?.Id);
        Assert.Equal(equipped[5], received.WeaponItem("OpenScct.Sidearm_unsuppressed")?.Id);
        foreach (var gun in MercPrimaryMastery.All.Where(g => g.HasSkinRewards))
        {
            var slot = CosmeticCatalog.WeaponSlot(gun.ClassPath)!.Value;
            Assert.Equal(gun.ClassPath, CosmeticCatalog.PrimaryClass(slot));
            Assert.Equal(6, CosmeticCatalog.For(CosmeticSide.Merc, slot).Count());
            var item = received.WeaponItem(gun.ClassPath);
            Assert.Equal(equipped.Single(s => s.StartsWith($"merc.{gun.Id}.", StringComparison.Ordinal)), item?.Id);
            Assert.Same(item, received.WeaponItem("OpenScctRifles." + gun.Id));
            var filtered = look.Validate(CosmeticSide.Merc, CosmeticUnlocks.Of(equipped.Except([item!.Id])));
            Assert.Null(filtered[slot]);
            Assert.Equal(equipped[0], filtered.WeaponItem(gun.ClassPath)?.Id);
        }
        Assert.True(CosmeticCatalog.Find("merc.famas.golden")!.Bit > byte.MaxValue);
        Assert.Equal(CosmeticLook.Default, received.Validate(CosmeticSide.Spy, ticket));
    }

    [Theory]
    [InlineData("Svm.CompactSMG", "compactsmg")]
    [InlineData("Svm.PumpShotgun", "pumpshotgun")]
    [InlineData("Svm.BoltRifle", "boltrifle")]
    [InlineData("Svm.DesignatedMarksman", "designatedmarksman")]
    public void DeferredModelsHaveNoCamoEvenWhenAllLegacyFamiliesAreDressed(string classPath, string id)
    {
        var look = CosmeticLook.FromIds(CosmeticSide.Merc, ["merc.ar.golden", "merc.smg.pyre", "merc.shotgun.blue_tiger"]);
        Assert.Null(CosmeticCatalog.WeaponSlot(classPath));
        Assert.Null(look.WeaponItem(classPath));
        Assert.Null(look.WeaponItem("OpenScctRifles." + id));
        Assert.DoesNotContain(CosmeticCatalog.All, item => item.Id.StartsWith($"merc.{id}.", StringComparison.Ordinal));
        Assert.Equal("merc.smg.pyre", look.WeaponItem("SWeaponFirst.Hk81")?.Id);
        Assert.Equal("merc.shotgun.blue_tiger", look.WeaponItem("SBase.SWeaponDefense_Shotgun")?.Id);
    }
}
