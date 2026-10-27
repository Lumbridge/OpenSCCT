using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// The server part of SBasePawn.Tick: a spy mark (spy bullet, merc laser, trap mine) wears off TimeSpyBullet seconds
// after it was put on, unless held (bForceSpied). Written from spec/equipment/spy-bullet.md rule 11.
public partial class SBasePawn
{
    private static readonly Name CNAME_TellClientToTestAndSetEMFVisible = new("TellClientToTestAndSetEMFVisible");

    /// <summary>
    /// The mark expiry (authority): the mark goes; when no pawn of this side is marked any more the other side stops
    /// listening; the other side is told (14 for a merc, 61 for a spy); the owner re-tests EMF visibility.
    /// </summary>
    [PortOf("uc:SBase.SBasePawn:Tick", Spec = "spec/equipment/spy-bullet.md#rules")]
    [Provenance(ProvenanceKind.Decoded, "SBasePawn.Tick: fTimeSpy != 0, !bForceSpied, now - fTimeSpy > TimeSpyBullet (90); messages 14 / 61 to team SPAWNDEFENSE - SPawnType")]
    private void SpyBulletExpiry()
    {
        if (Role != ENetRole.ROLE_Authority || fTimeSpy == 0f || bForceSpied || Level.TimeSeconds - fTimeSpy <= TimeSpyBullet)
        {
            return;
        }

        fTimeSpy = 0f;
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

        // OtherTeamCantHearMe is not ported: the voice port reads bSpyBulletOn (as in DetectedByMarqueurSpy).
        bool merc = SPawnType == PawnType.SPAWNDEFENSE;
        (Level.Game as SGameInfo)?.BroadcastTeamLocalizedMessage(
            (byte)(PawnType.SPAWNDEFENSE - SPawnType), GameMessagesClass, merc ? K.SpyBulletMsgSpyOff : K.SpyBulletMsgSpyUnlocked);
        TellClientToTestAndSetEMFVisible(false);
    }

    /// <summary>The owner's machine takes the mark flag and re-tests EMF visibility (server to owner).</summary>
    [Uc("SBase.SBasePawn.TellClientToTestAndSetEMFVisible", Sha1 = "f91a0cd90a94778ce6fa3d4fb6e3f57e08a905ce")]
    [PortOf("uc:SBase.SBasePawn:TellClientToTestAndSetEMFVisible", Spec = "spec/equipment/spy-bullet.md#rules")]
    [PortOf("rep:SBase.SBasePawn.TellClientToTestAndSetEMFVisible", Spec = "spec/equipment/spy-bullet.md#network")]
    public void TellClientToTestAndSetEMFVisible(bool bSpyBullet)
    {
        if (GadgetNet.ToOwner(Controller as SPlayerController, new CombatCall(CNAME_TellClientToTestAndSetEMFVisible)
        {
            Target = this,
            Bool1 = bSpyBullet,
        }))
        {
            return;
        }

        bSpyBulletOn = bSpyBullet;
        PawnVision.TestAndSetEMFVisible(this);
    }
}
