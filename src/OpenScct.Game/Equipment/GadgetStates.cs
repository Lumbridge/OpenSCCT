using OpenScct.Core;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScct.Game.Equipment;

/// <summary>The controller, HUD and gadget state names of the gadget ports.</summary>
public static class GadgetStates
{
    public static readonly Name s_UseObject = new("s_UseObject");
    public static readonly Name s_StickyCam_MenuInteraction = new("s_StickyCam_MenuInteraction");
    public static readonly Name s_UseObject_StickyCam_SmoothToPosition = new("s_UseObject_StickyCam_SmoothToPosition");
    public static readonly Name s_UseObject_StickyCam = new("s_UseObject_StickyCam");
    public static readonly Name s_UseObject_StickyCam_Leave = new("s_UseObject_StickyCam_Leave");
    public static readonly Name s_UseObject_CamNetworkCrWaitForServer = new("s_UseObject_CamNetworkCrWaitForServer");
    public static readonly Name s_UseObject_CamNetworkStWaitForServer = new("s_UseObject_CamNetworkStWaitForServer");
    public static readonly Name s_UseObject_CamNetwork = new("s_UseObject_CamNetwork");
    public static readonly Name s_BasePutMineOnWall = new("s_BasePutMineOnWall");
    public static readonly Name s_PutMineOnWall_Grab = new("s_PutMineOnWall_Grab");
    public static readonly Name s_PutMineOnWall_InteractionMenu = new("s_PutMineOnWall_InteractionMenu");
    public static readonly Name s_PutMineOnWall_UnGrab = new("s_PutMineOnWall_UnGrab");
    public static readonly Name s_PutMineOnWall = new("s_PutMineOnWall");
    public static readonly Name s_Binoculars_Att = new("s_Binoculars_Att");
    public static readonly Name s_PlayerWalkingMario_T_2HToOH = new("s_PlayerWalkingMario_T_2HToOH");
    public static readonly Name s_PipH_Fire4 = new("s_PipH_Fire4");

    // SHud states.
    public static readonly Name s_Hud_UseObject = new("s_Hud_UseObject");
    public static readonly Name s_Hud_UseObject_StickyCam = new("s_Hud_UseObject_StickyCam");
    public static readonly Name s_Hud_UseObject_CamNetwork = new("s_Hud_UseObject_CamNetwork");
    public static readonly Name s_H_Binoculars_Att = new("s_H_Binoculars_Att");
    public static readonly Name s_H_Binoculars_Def = new("s_H_Binoculars_Def");

    // Gadget and mine states.
    public static readonly Name s_Activated = new("s_Activated");
    public static readonly Name s_wait4boom = new("s_wait4boom");
    public static readonly Name s_Brouillage = new("s_Brouillage");
    public static readonly Name s_LockedByChaff = new("s_LockedByChaff");
    public static readonly Name s_Active = new("s_Active");
    public static readonly Name s_Inactive = new("s_Inactive");

    // Merc rifle states the mine clips drive.
    public static readonly Name s_DropMine_Grab = new("s_DropMine_Grab");
    public static readonly Name s_DropMine_Loop = new("s_DropMine_Loop");
    public static readonly Name s_DropMine = new("s_DropMine");
    public static readonly Name s_DropMine_UnGrab = new("s_DropMine_UnGrab");

    /// <summary>
    /// The mine-on-wall family (s_BasePutMineOnWall and the four states that extend it). The class metadata misses
    /// the extends of three of them, so the family is listed by name.
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "local/reflect/game.json state supers: s_PutMineOnWall, _Grab, _InteractionMenu, _UnGrab extend s_BasePutMineOnWall")]
    public static bool IsPuttingMineOnWall(SPlayerController PC)
    {
        Name s = PC.GetStateName();
        return s == s_BasePutMineOnWall || s == s_PutMineOnWall_Grab || s == s_PutMineOnWall_InteractionMenu ||
            s == s_PutMineOnWall_UnGrab || s == s_PutMineOnWall;
    }

    /// <summary>
    /// IsInState('s_UseObject_StickyCam'): the sticky cam view, or the camera network view that extends it (listed by
    /// name, as the metadata misses that extends).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "local/reflect/game.json: s_UseObject_CamNetwork extends s_UseObject_StickyCam")]
    public static bool InStickyCamViewState(SPlayerController PC) =>
        PC.IsInState(s_UseObject_StickyCam) || PC.IsInState(s_UseObject_CamNetwork);

    /// <summary>True in the sticky cam view (or the camera view) or the smooth entry.</summary>
    public static bool InStickyCamView(SPlayerController PC) =>
        InStickyCamViewState(PC) || PC.IsInState(s_UseObject_StickyCam_SmoothToPosition);
}
