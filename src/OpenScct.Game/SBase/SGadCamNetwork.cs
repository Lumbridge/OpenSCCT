using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SGadCamNetwork: the merc's CamNet (spec/equipment/camera-network-gadget.md rule 1): from anywhere, a walking
/// merc whose HUD is not perturbed goes behind the network's entry camera.
/// </summary>
public partial class SGadCamNetwork
{
    /// <summary>
    /// The level has a network camera to go behind (an OpenSCCT addition): without one the gadget does nothing and
    /// the HUD crosses its icon out.
    /// </summary>
    public bool HasNetwork => SCamNetwork.FindFirstCamera(XLevel) is not null;

    /// <summary>Nothing on a tutorial map; a walking, unperturbed merc waits for the server and asks for the first camera.</summary>
    [Uc("SBase.SGadCamNetwork.Use", Sha1 = "0e5864c28b552eeb2786598a7c4193f5645e8c6f")]
    [PortOf("uc:SBase.SGadCamNetwork:Use", Spec = "spec/equipment/camera-network-gadget.md#rules")]
    public override void Use(float Value)
    {
        if (GadgetWorld.CurLevelIsTutorial(this))
        {
            return;
        }

        // An OpenSCCT fix: on a level with no network camera (the hub) the original still enters the wait-for-server
        // state, the server finds no camera to hand over and the merc stays frozen in it. Nothing happens instead.
        if (!HasNetwork)
        {
            return;
        }

        if (OwnerPawn is { Physics: EPhysics.PHYS_Walking } pawn && pawn.Controller is SPlayerController pc && !pc.IsHudPerturbed)
        {
            pc.eventChangeState(pawn.bIsCrouched ? GadgetStates.s_UseObject_CamNetworkCrWaitForServer : GadgetStates.s_UseObject_CamNetworkStWaitForServer);
            pc.UseCamNetwork(0);
        }

        base.Use(Value);
    }
}
