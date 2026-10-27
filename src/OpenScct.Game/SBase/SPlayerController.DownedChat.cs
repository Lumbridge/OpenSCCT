using OpenScct.Core;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController downed chat (spec/game/voice-chat.md rule 9): while a player lies on the ground, the server
// keeps up to three enemies near him who hold the voice key as chat partners, and HearPC routes the voice for every
// change. This is how a merc talks to the spy he is finishing (the spy stays bLyingOnGround through s_TerminatedPawn).
public partial class SPlayerController
{
    /// <summary>A state version of <see cref="HearPC"/>.</summary>
    public interface IHearPC
    {
        void HearPC(SPlayerController self, SPlayerController PCToTalkWith, bool bHear);
    }

    /// <summary>HearPC through the current state.</summary>
    public void eventHearPC(SPlayerController PCToTalkWith, bool bHear)
    {
        if (StateNode is IHearPC s)
        {
            s.HearPC(this, PCToTalkWith, bHear);
        }
        else
        {
            HearPC(PCToTalkWith, bHear);
        }
    }

    [Uc("SBase.SPlayerController.HearPC", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
    [PortOf("uc:SBase.SPlayerController:HearPC", Spec = "spec/game/voice-chat.md#rules")]
    public void HearPC(SPlayerController PCToTalkWith, bool bHear)
    {
    }

    /// <summary>
    /// s_Inactive.HearPC: this downed player hears (or stops hearing) the partner, and the partner's own team stops
    /// (or starts again) hearing the partner.
    /// </summary>
    [Uc("SBase.SPlayerController.s_Inactive.HearPC", Sha1 = "52d05e9174df778c0b0a37efb219ae1ddd1edbce")]
    [PortOf("uc:SBase.SPlayerController:s_Inactive.HearPC", Spec = "spec/game/voice-chat.md#rules")]
    internal void InactiveHearPC(SPlayerController PCToTalkWith, bool bHear)
    {
        if (SGame is not { } game)
        {
            return;
        }

        Ports.HearVoiceChatter(game, this, PCToTalkWith, !bHear);
        foreach (SPlayerController pc in XLevel.AllActors<SPlayerController>())
        {
            if (pc.SPawnType == PCToTalkWith.SPawnType && !ReferenceEquals(pc, PCToTalkWith))
            {
                Ports.HearVoiceChatter(game, pc, PCToTalkWith, bHear);
            }
        }
    }

    /// <summary>
    /// The downed chat part of the controller's tick (server, pawn bLyingOnGround). A partner is dropped when he is
    /// farther than fDistToTalkWhenOnGround or lets go of the voice key; an enemy with a pawn who holds the key within
    /// that range takes the first free slot of the three while this pawn is alive.
    /// </summary>
    [UcNative("ASPlayerController::Tick", Editor = 0x111f41b0)]
    [Provenance(ProvenanceKind.Decoded, "editor 0x111f1740, called from ASPlayerController::Tick with Role 4 when the pawn is bLyingOnGround")]
    private void UpdateDownedChat()
    {
        if (Pawn is not { } me)
        {
            return;
        }

        float range = fDistToTalkWhenOnGround;
        for (int i = 0; i < PCChattingWith.Length; i++)
        {
            if (PCChattingWith[i] is { Pawn: { } its } partner && partner.SPawnType != SPawnType &&
                (FVector.Dist(its.Location, me.Location) > range || partner.bSpecialsChat == 0))
            {
                eventHearPC(partner, false);
                PCChattingWith[i] = null;
            }
        }

        if (me.Health <= 0)
        {
            return;
        }

        foreach (SPlayerController pc in XLevel.AllActors<SPlayerController>())
        {
            if (pc.SPawnType == SPawnType || pc.Pawn is not { } its || pc.bSpecialsChat == 0 ||
                !(FVector.Dist(its.Location, me.Location) < range) || IsChatPartner(pc))
            {
                continue;
            }

            for (int i = 0; i < PCChattingWith.Length; i++)
            {
                if (PCChattingWith[i] is null)
                {
                    PCChattingWith[i] = pc;
                    eventHearPC(pc, true);
                    break;
                }
            }
        }
    }

    private bool IsChatPartner(SPlayerController pc)
    {
        for (int i = 0; i < PCChattingWith.Length; i++)
        {
            if (ReferenceEquals(PCChattingWith[i], pc))
            {
                return true;
            }
        }

        return false;
    }
}
