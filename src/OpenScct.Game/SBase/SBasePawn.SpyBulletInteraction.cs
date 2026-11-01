using OpenScct.Core.Engine;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SBasePawn.DetectedByMarqueurSpy, the native that puts a spy mark on a pawn or takes it off (the Unspy Mate
// entry, the spy bullet projectile and the respawn clean-up call it with bActivate false). Written from
// spec/objectives/team-interactions.md (unspy mate).
public partial class SBasePawn
{
    /// <summary>
    /// With <paramref name="bActivate"/>: marked and held marked (bSpyBulletOn, bForceSpied), every controller of
    /// the other side may listen to this pawn, iTeamSpying = <paramref name="Team"/>, fTimeSpy = now, then the SetEMF
    /// event. Without: only when marked, the mark and the hold are cleared; when no pawn of this side is marked any
    /// more the other side's controllers stop listening; the controller's OtherTeamCantHearMe event; and fTimeSpy is
    /// set TimeSpyBullet seconds back, so the pawn's tick ends the mark at once (its expiry message and clean-up).
    /// </summary>
    [UcNative("ASBasePawn::execDetectedByMarqueurSpy", Exe = 0x10AE70C0)]
    [PortOf("nat:SBase.SBasePawn.DetectedByMarqueurSpy", Spec = "spec/objectives/team-interactions.md#unspy-mate")]
    [Provenance(ProvenanceKind.Decoded, "Game execDetectedByMarqueurSpy 0x10ae70c0; bits from local/reflect (bSpyBulletOn 0xaf4 bit 0, bForceSpied 0xaf0 bit 3, bCanListenSpyBulletedGuy 0xc38 bit 2)")]
    public void DetectedByMarqueurSpy(int Team, bool bActivate)
    {
        using var clock = WorldEventTime();
        if (bActivate)
        {
            bSpyBulletOn = true;
            bForceSpied = true;
            foreach (SPlayerController pc in XLevel.AllActors<SPlayerController>())
            {
                if (pc.SPawnType != SPawnType)
                {
                    pc.bCanListenSpyBulletedGuy = true;
                }
            }

            iTeamSpying = Team;
            fTimeSpy = Level.TimeSeconds;
            PawnVision.SetEMF(this);
            return;
        }

        if (!bSpyBulletOn)
        {
            return;
        }

        bForceSpied = false;
        bSpyBulletOn = false;
        bool anyMarked = false;
        foreach (SBasePawn pawn in GadgetWorld.Pawns(XLevel))
        {
            if (pawn.SPawnType == SPawnType && pawn.bSpyBulletOn)
            {
                anyMarked = true;
                break;
            }
        }

        if (!anyMarked)
        {
            foreach (SPlayerController pc in XLevel.AllActors<SPlayerController>())
            {
                if (pc.SPawnType != SPawnType)
                {
                    pc.bCanListenSpyBulletedGuy = false;
                }
            }
        }

        // The controller's OtherTeamCantHearMe event is not ported: the voice port reads bSpyBulletOn itself
        // (OpenScct.Net VoiceRoster), so taking the mark off already closes the other side's channel.
        fTimeSpy = Level.TimeSeconds - TimeSpyBullet;
    }
}
