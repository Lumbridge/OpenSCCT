using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController exec DisplayStrategicMap, the state part, and the page's position request: the page itself
// (StopAllControls, ClientOpenMenu('StrategicMap_InGame')) is the play scene's (game/Scripts/Play/PlayMenu.cs), which
// asks here first. Written from spec/hud/strategic-map.md.
public partial class SPlayerController
{
    private static readonly Name CNAME_UpdateMinimapParams = new("UpdateMinimapParams");

    /// <summary>
    /// The DisplayStrategicMap key (F7 in both stock layouts) on the player's own machine: true when the strategic map
    /// may open. Not on a dedicated server's own controller, not on a tutorial map, and not while interacting with an
    /// object (s_InteractWithObject ignores the key).
    /// </summary>
    [Uc("SBase.SPlayerController.DisplayStrategicMap", Sha1 = "152b2362073afb2e16e50399ead3c911bc1286d2")]
    [PortOf("uc:SBase.SPlayerController:DisplayStrategicMap", Spec = "spec/hud/strategic-map.md#rules")]
    public bool CanDisplayStrategicMap()
    {
        if (GameHost.IsDedicatedServerMode() || GadgetWorld.CurLevelIsTutorial(this))
        {
            return false;
        }

        Name s = GetStateName();
        return s != CNAME_s_InteractWithObject && s != CNAME_s_MenuInteraction_GoTo && s != CNAME_s_InteractWithObjectWaitingEvent &&
            s != CNAME_s_InteractWithObjectWaitingEventCrToSt;
    }

    /// <summary>
    /// The strategic map's request for positions (<c>PlayerReplicationInfo.UpdateMinimapParams</c>, reliable client to
    /// server): run on the server for this player's PRI; a client sends it.
    /// </summary>
    public void RequestMinimapParams()
    {
        if (Role < ENetRole.ROLE_Authority)
        {
            CombatNet.ServerCall(this, new CombatCall(CNAME_UpdateMinimapParams));
            return;
        }

        PlayerReplicationInfo?.UpdateMinimapParams();
    }
}
