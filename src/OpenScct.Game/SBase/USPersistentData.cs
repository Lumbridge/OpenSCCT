using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

/// <summary>
/// SBase.SPersistentData: the host's settings object (spec/equipment/loadouts.md rule 7). A new object holds the
/// class defaults; <see cref="Init"/> is the engine's creation event that gives it the original's starting values.
/// </summary>
public partial class USPersistentData
{
    /// <summary>The engine's creation event: the lobby voice and friend flags, then the gameplay defaults.</summary>
    [Uc("Engine.PersistentData.Init", Sha1 = "6abb4052b6945f6af930b957bbb3e9ed305dae89")]
    [PortOf("uc:Engine.PersistentData:Init", Spec = "spec/equipment/loadouts.md#rules")]
    public void Init()
    {
        allowVoiceChatInLobby = true;
        friendMustBlink = false;
        InitPersistentData();
    }

    /// <summary>
    /// The default abilities, gadget restrictions (every option allowed) and gadget counts (4 a side), then the SBase
    /// part: no cinematic camera.
    /// </summary>
    [Uc("Engine.PersistentData.InitPersistentData", Sha1 = "da3108cf257706b657e60529c57966cd2be901a8")]
    [Uc("SBase.SPersistentData.InitPersistentData", Sha1 = "854d8b35e356f292b3933d937894b77cf9482259")]
    [PortOf("uc:Engine.PersistentData:InitPersistentData", Spec = "spec/equipment/loadouts.md#rules")]
    [PortOf("uc:SBase.SPersistentData:InitPersistentData", Spec = "spec/equipment/loadouts.md#rules")]
    public void InitPersistentData()
    {
        bAvlb_SPY_ThermalVision = true;
        bAvlb_SPY_NightVision = true;
        bAvlb_SPY_Grab = true;
        bAvlb_SPY_Elbow = true;
        bAvlb_SPY_LeathalDropTech = false;
        bAvlb_MERC_Elbow = true;
        bAvlb_MERC_LeathalDropTech = false;
        bAvlb_MERC_EMFVision = true;
        bAvlb_MERC_MotiontrackingVision = true;
        bAvlb_MERC_Torche = true;
        bAvlb_MERC_Laser = true;
        bAvlb_MERC_Berserk = true;
        bAvlb_MERC_Charge = true;
        b_SPY_GadgetRestriction = Equipment.K.AllGadgetsAllowed;
        b_MERC_GadgetRestriction = Equipment.K.AllGadgetsAllowed;
        b_SPY_NBGadget = (byte)Equipment.K.GadgetSlots;
        b_MERC_NBGadget = (byte)Equipment.K.GadgetSlots;
        bAvlb_CinematicCamera = false;
    }

    /// <summary>A settings object as the engine makes it (class defaults, then <see cref="Init"/>).</summary>
    public static USPersistentData CreateInitialized()
    {
        var pd = new USPersistentData();
        pd.Init();
        return pd;
    }
}
