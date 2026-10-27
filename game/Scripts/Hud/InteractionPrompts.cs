using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.SBase;
using OpenScct.Provenance;

namespace OpenScctGame.Hud;

/// <summary>
/// The spy page's coop icons (A_Coop: Move_coop to Move_coop4), as the native spy HUD driver picks them
/// (spec/objectives/team-interactions.md, HUD prompts). Read only: the queries it makes write nothing.
/// </summary>
public static class InteractionPrompts
{
    private static readonly Name NAME_s_Hack_Use = new("s_Hack_Use");
    private static readonly Name NAME_s_Computer_Use = new("s_Computer_Use");

    /// <summary>
    /// None while hacking a terminal or a switch, in a tutorial level, or while the own pawn is a ready helper.
    /// Otherwise by the mate search (its quick form, no boost spot test): a ready boost helper in reach gives
    /// "climb on him" (Move_coop2), a mate hanging ready for a hoist "be boosted up" (Move_coop4); with neither, a
    /// walking pawn at a boost spot gets "offer a ladder" (Move_coop) and a pawn hanging where a hoist is possible
    /// "offer a hoist" (Move_coop3).
    /// </summary>
    [Provenance(ProvenanceKind.Decoded, "Spy HUD coop driver game 0x10b2db20: state names s_Hack_Use / s_Computer_Use, CurLevelIsTutorial, bReadyFor* bits 0x3, CheckIfCanInteractWithMate(mate, 1), Physics 1 + FUN_10ae6d40, Physics 9 + FUN_10ae5b50")]
    public static HudCoopIcon Coop(SPlayerController pc, SBasePawn? pawn)
    {
        if (pawn is null || pc.IsInState(NAME_s_Hack_Use) || pc.IsInState(NAME_s_Computer_Use) || pc.CoopTutorialLevel() ||
            pawn.bReadyForShortLadder || pawn.bReadyForHoistClimb)
        {
            return HudCoopIcon.None;
        }

        switch (pc.CheckIfCanInteractWithMate(out _, true))
        {
            case SPlayerController.CA_ShortLadder:
                return HudCoopIcon.ClimbMate;
            case SPlayerController.CA_HoistClimb:
                return HudCoopIcon.BoostedUp;
            case SPlayerController.CA_None:
                if (pawn.Physics == EPhysics.PHYS_Walking && pawn.CheckIfCanStartSHL(out _, out _))
                {
                    return HudCoopIcon.OfferLadder;
                }

                if (pawn.Physics == EPhysics.PHYS_Ledge && pawn.FindHoistClimbType() > 0)
                {
                    return HudCoopIcon.LedgeHoist;
                }

                return HudCoopIcon.None;
            default:
                return HudCoopIcon.None;
        }
    }
}
