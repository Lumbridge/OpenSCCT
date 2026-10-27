using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn.NotifyQuickLaunch: the script notify of the quick throw clip ThroStAlMix (at 0.4 s), also raised
// by the actions that cancel a running quick throw (spec/equipment/grenade-throw.md rule 6,
// spec/native/execute-action.md case 251).
public partial class SBasePawn
{
    /// <summary>The script notify name of the quick throw clip.</summary>
    public static readonly Name NAME_NotifyQuickLaunch = new("NotifyQuickLaunch");

    /// <summary>
    /// The quick throw's launch frame: on the controlling machine (the controller's RemoteRole is not
    /// AutonomousProxy) the controller's RealGadgetQuickLaunch, then bQuickGadgetHasBegun cleared on every machine.
    /// </summary>
    [Uc("SBase.SBasePawn.NotifyQuickLaunch", Sha1 = "cf09d96ff1d8880cb6287d0ad39b8c69da3fae01")]
    [PortOf("uc:SBase.SBasePawn:NotifyQuickLaunch", Spec = "spec/equipment/grenade-throw.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.uc NotifyQuickLaunch: Controller != None and RemoteRole != ROLE_AutonomousProxy, RealGadgetQuickLaunch, bQuickGadgetHasBegun = false")]
    public virtual void NotifyQuickLaunch()
    {
        if (Controller is SPlayerController pc && pc.IAmOnClient())
        {
            pc.RealGadgetQuickLaunch();
        }

        bQuickGadgetHasBegun = false;
    }

    /// <summary>
    /// The cancel of a running quick throw by a jump or a grab action (ExecuteAction cases 90, 91, 126, 143, 153,
    /// 172, 196, 199, 204 and 283, execute-action.md): the palm channel blends out over 0.1 s, the flag is
    /// cleared, then NotifyQuickLaunch is raised, so the grenade a cancel throws takes the force without the clip
    /// flag (20).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "ASBasePawn::ExecuteAction game 0x10ad6840 cases 0x5a/0x5b, 0x7e, 0x8f, 0x99, 0xac, 0xc4, 0xc7, 0xcc, 0x11b (REA 2026-10-26): BlendToAlpha(22, 0, 0.1), bits & ~0x10, then eventNotifyQuickLaunch (0x10ae7570, name 0xa30e2)")]
    public void CancelQuickGadget()
    {
        if (!bQuickGadgetHasBegun)
        {
            return;
        }

        PawnAnim.BlendToAlpha(PALMCHANNEL, 0f, 0.1f);
        bQuickGadgetHasBegun = false;
        NotifyQuickLaunch();
    }
}
