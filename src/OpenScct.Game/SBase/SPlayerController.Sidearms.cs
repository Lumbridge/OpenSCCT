using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;

namespace OpenScct.Game.SBase;

public partial class SPlayerController
{
    private int _weaponRequest;
    private int _weaponRequestAccepted;
    public void SwapWeapon()
    {
        if (SBPawn is not { } pawn || !SidearmEquipment.CanSwitch(pawn)) return;
        int slot = pawn.ActiveWeaponSlot == 0 ? 1 : 0;
        if (Role == ENetRole.ROLE_Authority) { SidearmEquipment.Select(pawn, slot); return; }
        SidearmEquipment.Select(pawn, slot);
        CombatServices.Net(this).ServerCall(this, new CombatCall(new Name("SelectCarriedWeapon"))
            { Target = pawn, Int1 = slot, Int2 = ++_weaponRequest });
    }

    internal void ReceiveWeaponSelection(int slot, int sequence)
    {
        if (SBPawn is not { } pawn) return;
        bool accepted = sequence > _weaponRequestAccepted && SidearmEquipment.Select(pawn, slot);
        _weaponRequestAccepted = System.Math.Max(_weaponRequestAccepted, sequence);
        CombatServices.Net(this).ClientCall(this, new CombatCall(new Name("CarriedWeaponSelected"))
        {
            Target = pawn, Int1 = pawn.ActiveWeaponSlot, Int2 = sequence, Bool1 = accepted,
            Float1 = System.Math.Max(0, pawn.WeaponSwitchUntil - pawn.Level.TimeSeconds),
        });
    }
}
