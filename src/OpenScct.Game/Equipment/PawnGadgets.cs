using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Animation;
using OpenScct.Game.Combat;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>
/// The SBasePawn functions of the gadgets (spec/equipment/camera-network-gadget.md rule 5, gas-mask.md rule 2,
/// mine-launcher.md rule 5): the camera vision calls, the gas mask flag, and the clip's interaction notify. They are
/// SBasePawn units kept with the equipment port.
/// </summary>
public static class PawnGadgets
{
    private static readonly Name CNAME_CamNetworkSwitchVisionModeOnServer = new("CamNetworkSwitchVisionModeOnServer");
    private static readonly Name CNAME_CamNetworkSwitchVisionModeOnAllClients = new("CamNetworkSwitchVisionModeOnAllClients");
    private static readonly Name CNAME_SetGasMaskUseOnServer = new("SetGasMaskUseOnServer");

    /// <summary>The script notify name of the mine placement clip's interaction frame.</summary>
    public static readonly Name AnimNotify_Interact = new("AnimNotify_Interact");

    /// <summary>The camera's vision mode, asked of the server (client to server).</summary>
    [Uc("SBase.SBasePawn.CamNetworkSwitchVisionModeOnServer", Sha1 = "d37b00a485ed62d496d39a58315a76f0e0a1b4f5")]
    [PortOf("uc:SBase.SBasePawn:CamNetworkSwitchVisionModeOnServer", Spec = "spec/equipment/camera-network-gadget.md#rules")]
    public static void CamNetworkSwitchVisionModeOnServer(SBasePawn Pawn, SCamNetwork? Cam, int CurrentMode)
    {
        if (GadgetNet.ToServer(Pawn, Pawn.Controller as PlayerController,
            new CombatCall(CNAME_CamNetworkSwitchVisionModeOnServer) { Target = Pawn, Actor1 = Cam, Int1 = CurrentMode }))
        {
            return;
        }

        CamNetworkSwitchVisionModeOnAllClients(Pawn, Cam, CurrentMode);
    }

    /// <summary>
    /// The camera's vision switch on every machine (the "to all clients" flag): here, and on the pawn's owner. Other
    /// clients follow the camera's replicated vision mode.
    /// </summary>
    [Uc("SBase.SBasePawn.CamNetworkSwitchVisionModeOnAllClients", Sha1 = "3eebaeb0d3f9fcf8c7b191ad2df17594c56b6ccb")]
    [PortOf("uc:SBase.SBasePawn:CamNetworkSwitchVisionModeOnAllClients", Spec = "spec/equipment/camera-network-gadget.md#rules")]
    [Provenance(ProvenanceKind.Estimated, "The all-clients call reaches the owner here; the other clients use iReplicatedVisionMode")]
    public static void CamNetworkSwitchVisionModeOnAllClients(SBasePawn Pawn, SCamNetwork? Cam, int CurrentMode)
    {
        GadgetNet.ToOwner(Pawn.Controller as PlayerController,
            new CombatCall(CNAME_CamNetworkSwitchVisionModeOnAllClients) { Target = Pawn, Actor1 = Cam, Int1 = CurrentMode });
        Cam?.SwitchToVision(CurrentMode);
    }

    /// <summary>The server learns the gas mask flag (client to server).</summary>
    [Uc("SBase.SBasePawn.SetGasMaskUseOnServer", Sha1 = "73670af801d78e21a245c71f2d9f5395afa46302")]
    [PortOf("uc:SBase.SBasePawn:SetGasMaskUseOnServer", Spec = "spec/equipment/gas-mask.md#rules")]
    public static void SetGasMaskUseOnServer(SBasePawn Pawn, bool val)
    {
        if (GadgetNet.ToServer(Pawn, Pawn.Controller as PlayerController,
            new CombatCall(CNAME_SetGasMaskUseOnServer) { Target = Pawn, Bool1 = val }))
        {
            return;
        }

        Pawn.bGasMask = val;
    }

    /// <summary>The interaction frame of a clip (AnimNotify_Interact): the controller's AnimNotification_InteractNow.</summary>
    [Uc("SBase.SBasePawn.AnimNotify_Interact", Sha1 = "f4751a5564a1999ad6b141d669b202d06b383965")]
    [PortOf("uc:SBase.SBasePawn:AnimNotify_Interact", Spec = "spec/equipment/mine-launcher.md#placing")]
    public static void AnimNotifyInteract(Actor Owner)
    {
        if (Owner is SBasePawn { Controller: SPlayerController pc })
        {
            pc.eventAnimNotificationInteractNow();
        }
    }

    /// <summary>Registers the gadget clip notifies on a level's notify handlers (the interaction frame, the quick throw).</summary>
    public static void RegisterNotifies(Level Level) =>
        AnimNotifyHandlers.Of(Level)
            .Register(AnimNotify_Interact, AnimNotifyInteract)
            .Register(SBasePawn.NAME_NotifyQuickLaunch, a => (a as SBasePawn)?.NotifyQuickLaunch());
}
