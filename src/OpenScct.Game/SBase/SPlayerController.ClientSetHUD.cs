using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SPlayerController.ClientSetHUD (spec/equipment/gadget-hud.md rule 1): the server's login call that gives the
/// player's machine its SHud. The gadget views, the HUD message stacks, the HUD perturbation and the binocular states
/// all live on that actor.
/// </summary>
public partial class SPlayerController
{
    /// <summary>
    /// A new HUD of <paramref name="newHUDType"/> when there is none or it is another class (the old one goes), its
    /// scoreboard, then the HUD's Init (the gadget selection fields cleared).
    /// </summary>
    [Uc("SBase.SPlayerController.ClientSetHUD", Sha1 = "61edc95f151202cdb89b9ab0d68b264c3ac4c7f6")]
    [PortOf("uc:SBase.SPlayerController:ClientSetHUD", Spec = "spec/equipment/gadget-hud.md#shud-states")]
    [Provenance(ProvenanceKind.Estimated, "HUD.PostBeginPlay's PlayerOwner is set here; the scoreboard class is not spawned (no scoreboard port)")]
    public void ClientSetHUD(ClassRef newHUDType, ClassRef newScoringType)
    {
        if (myHUD is null || (!newHUDType.IsNone && newHUDType.Path != myHUD.GetUcClass().Path))
        {
            if (UcSpawn.Spawn(this, newHUDType, this, index: Devices.DeviceClasses.Index) is HUD newHud)
            {
                myHUD?.Destroy();
                myHUD = newHud;
                newHud.PlayerOwner = this;
            }
        }

        if (myHUD is null)
        {
            return;
        }

        // HUD.Init.
        myHUD.LastSwitchGadget = 0;
        myHUD.bGadgetJustSelected = false;
    }
}
