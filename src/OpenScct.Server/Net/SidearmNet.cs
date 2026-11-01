using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Game.SBase;
using OpenScct.Net;

namespace OpenScct.Server;

public static class SidearmNet
{
    private static readonly RepCondition PrimaryWeaponVisible = RepCondition.Bind(
        RepExpr.Or(RepExpr.Net(RepNetVar.bNetOwner), RepExpr.Ne(RepExpr.Prop(nameof(SBasePawn.SidearmWeapon)), RepExpr.Null)),
        SBasePawn.UcMeta.Class, new PropertyReaders());

    public static void Register(NetClassRegistry registry)
    {
        var carry = new RepExtension();
        // Include the expanded catalogue in the protocol signature: older clients clamp the fifth kind to Desert Eagle.
        carry.Properties.Add(new("SidearmKind5", nameof(SidearmCarry.Kind), RepLeafKind.Int));
        registry.Register<SidearmCarry>(carry);
        var pistol = new RepExtension();
        pistol.Properties.Add(new("SidearmKind5", nameof(MercSidearm.Kind), RepLeafKind.Int));
        pistol.Properties.Add(new("SidearmFireMode", nameof(MercSidearm.SidearmFireMode), RepLeafKind.Enum));

        registry.Register<MercSidearm>(pistol);
        var dummy = new RepExtension();
        dummy.Properties.Add(new("SidearmKind5", nameof(MercSidearmDummy.Kind), RepLeafKind.Int));
        registry.Register<MercSidearmDummy>(dummy);
        for (int i = 0; i < registry.Count; i++)
        {
            if (!typeof(SWeaponDefense).IsAssignableFrom(registry.Classes[i].Type)) continue;
            var ammo = new RepExtension();
            ammo.Properties.Add(new("CarriedDummy", nameof(SWeaponDefense.Dummy), RepLeafKind.Object));
            ammo.Properties.Add(new("CarriedClip", nameof(SWeaponDefense.CarriedClip), RepLeafKind.Int));
            ammo.Properties.Add(new("CarriedAmmo", nameof(SWeaponDefense.CarriedAmmo), RepLeafKind.Int));
            ammo.Properties.Add(new("CarriedShot", nameof(SWeaponDefense.CarriedShot), RepLeafKind.Int));
            ammo.Properties.Add(new("CarriedAmmoRevision", nameof(SWeaponDefense.CarriedAmmoRevision), RepLeafKind.Int));
            registry.Extend(registry.Classes[i].Type, ammo);
        }
    }

    public static RepExtension PawnExtension()
    {
        var ext = new RepExtension();
        // A single rifle's first-person actor is only relevant to its owner; observers use WeaponDummy.
        // Granting a sidearm makes both carried weapons follow the pawn, so observers can then resolve them.
        // Sending the hidden rifle to other clients otherwise retries an unmappable reference every tick.
        ext.Properties.Add(new("PrimaryWeapon", nameof(SBasePawn.PrimaryWeapon), RepLeafKind.Object, PrimaryWeaponVisible));
        ext.Properties.Add(new("SidearmWeapon", nameof(SBasePawn.SidearmWeapon), RepLeafKind.Object));
        ext.Properties.Add(new("ActiveWeaponSlot", nameof(SBasePawn.ActiveWeaponSlot), RepLeafKind.Int));
        ext.Properties.Add(new("WeaponSwitching", nameof(SBasePawn.WeaponSwitching), RepLeafKind.Bool));
        return ext;
    }
}
