using OpenScct.Core;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController exec ShowMenu, the state part: the In-Game Menu itself (StopAllControls, ClientOpenMenu) is
// the play scene's (game/Scripts/Play/PlayMenu.cs), which asks here first. Written from spec/hud/in-game-menu.md
// (rule 1a). The states are listed by name because the class metadata misses the extends of most of them.
public partial class SPlayerController
{
    private static readonly Name CNAME_s_InteractWithObject = new("s_InteractWithObject");
    private static readonly Name CNAME_s_MenuInteraction_GoTo = new("s_MenuInteraction_GoToInteractionPosition");
    private static readonly Name CNAME_s_InteractWithObjectWaitingEvent = new("s_InteractWithObjectWaitingEvent");
    private static readonly Name CNAME_s_InteractWithObjectWaitingEventCrToSt = new("s_InteractWithObjectWaitingEventCrToSt");
    private static readonly Name CNAME_s_MenuInteraction = new("s_MenuInteraction");
    private static readonly Name CNAME_s_Binoculars_DefMenu = new("s_Binoculars_Def");
    private static readonly Name CNAME_s_Binoculars_Def_ReloadingMenu = new("s_Binoculars_Def_Reloading");

    /// <summary>
    /// The state part of the ShowMenu key on the player's own machine; true when the In-Game Menu opens. The
    /// interactions ignore the key (the interaction menu only while Use is up); the mine-on-wall states put the mine
    /// launcher down first, the scope lets the breath go, the sticky cam menu closes first.
    /// </summary>
    [Uc("SBase.SPlayerController.s_MenuInteraction.ShowMenu", Sha1 = "dc8d26c700fc4b10a050fdc653996b6dce927b3a")]
    [Uc("SBase.SPlayerController.s_BasePutMineOnWall.ShowMenu", Sha1 = "992b16f1e6996b9306b441ddfdf9ee8e65d988b5")]
    [Uc("SBase.SPlayerController.s_Binoculars_Def.ShowMenu", Sha1 = "d3e1b315066cb44ad7c798b57feb5b7356d23b29")]
    [Uc("SBase.SPlayerController.s_StickyCam_MenuInteraction.ShowMenu", Sha1 = "dca07bfe3d1ad62b1c0fc4d7d48d4289529c54a3")]
    [PortOf("uc:SBase.SPlayerController:s_MenuInteraction.ShowMenu", Spec = "spec/hud/in-game-menu.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_BasePutMineOnWall.ShowMenu", Spec = "spec/hud/in-game-menu.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_Binoculars_Def.ShowMenu", Spec = "spec/hud/in-game-menu.md#rules")]
    [PortOf("uc:SBase.SPlayerController:s_StickyCam_MenuInteraction.ShowMenu", Spec = "spec/hud/in-game-menu.md#rules")]
    public bool eventShowMenu()
    {
        Name s = GetStateName();
        if (s == CNAME_s_InteractWithObject || s == CNAME_s_MenuInteraction_GoTo || s == CNAME_s_InteractWithObjectWaitingEvent ||
            s == CNAME_s_InteractWithObjectWaitingEventCrToSt)
        {
            return false;
        }

        if (s == CNAME_s_MenuInteraction)
        {
            return bUsing == 0;
        }

        if (GadgetStates.IsPuttingMineOnWall(this))
        {
            if (s != GadgetStates.s_PutMineOnWall_UnGrab)
            {
                eventChangeState(GadgetStates.s_PutMineOnWall_UnGrab);
            }

            return true;
        }

        if (s == CNAME_s_Binoculars_DefMenu || s == CNAME_s_Binoculars_Def_ReloadingMenu)
        {
            bHoldBreath = 0;
            return true;
        }

        if (s == GadgetStates.s_StickyCam_MenuInteraction)
        {
            GotoWaitingState();
        }

        return true;
    }
}
