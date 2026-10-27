using OpenScct.Core;
using OpenScct.Core.Uc;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// The malfunction markers of gameplay objects (SBasicGamePlayObject.AddOnHud / RemoveFromHud): the alarms' devices and
// the presence detector's warning. Server to owner, unreliable.
public partial class SPlayerController
{
    private static readonly Name CNAME_AddGPOOnHud = new("AddGPOOnHud");
    private static readonly Name CNAME_RemoveGPOFromHud = new("RemoveGPOFromHud");

    /// <summary>A marker at <paramref name="GPOLocation"/> on this player's HUD (the spy's GPO marker, the merc's radar alarm).</summary>
    [Uc("SBase.SPlayerController.AddGPOOnHud", Sha1 = "55c429606431284ea0f98be84dd1099f98aac845")]
    [PortOf("uc:SBase.SPlayerController:AddGPOOnHud", Spec = "spec/devices/gameplay-object.md#effects-and-helpers")]
    public void AddGPOOnHud(FVector GPOLocation)
    {
        if (GadgetNet.ToOwner(this, new CombatCall(CNAME_AddGPOOnHud) { Vector1 = GPOLocation }))
        {
            return;
        }

        (myHUD as SHud)?.AddMalfunctionGPO(GPOLocation);
    }

    /// <summary>Removes the marker at <paramref name="GPOLocation"/>.</summary>
    [Uc("SBase.SPlayerController.RemoveGPOFromHud", Sha1 = "be0e8649642a3db48bf66540347b48c48b4b75c7")]
    [PortOf("uc:SBase.SPlayerController:RemoveGPOFromHud", Spec = "spec/devices/gameplay-object.md#effects-and-helpers")]
    public void RemoveGPOFromHud(FVector GPOLocation)
    {
        if (GadgetNet.ToOwner(this, new CombatCall(CNAME_RemoveGPOFromHud) { Vector1 = GPOLocation }))
        {
            return;
        }

        (myHUD as SHud)?.RemoveMalfunctionGPO(GPOLocation);
    }
}
