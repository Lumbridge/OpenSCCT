using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Game.Combat;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController damage while looking through a sticky cam (or a network camera, whose view state extends the
// sticky cam view): the fly-in cleans the owner's view and leaves the cam, the view goes back to the state the player
// came from. Written from spec/equipment/sticky-cam.md rule 13 and spec/combat/state-damage-reactions.md.
public partial class SPlayerController
{
    private static readonly Name CNAME_s_PlayerWaiting = new("s_PlayerWaiting");
    private static readonly Name CNAME_s_PlayerChargingPrev = new("s_PlayerCharging");

    /// <summary>
    /// The cam view (and the camera network view) hit: the charge, elbow and drop reactions, else back to the state
    /// the view was entered from; a previous state that cannot be returned to becomes the resting state.
    /// </summary>
    [Uc("SBase.SPlayerController.s_UseObject_StickyCam.PawnTakeDamage", Sha1 = "dcd9ee7d48827d31848b90445c749768693dd19a")]
    [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam.PawnTakeDamage", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
    private void StickyCamViewDamage(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        if (SBPawn is not { } pawn)
        {
            return;
        }

        if (pawn.Health <= 0)
        {
            CombatDeadDirectOrFatal(T, M);
            return;
        }

        switch (T)
        {
            case SDamageType.SDTStickyShocker:
                eventCheckshock(Pill, true);
                break;
            case SDamageType.SDamage_Charged:
                Handle_ChargedDmg(M, By);
                return;
            case SDamageType.SDamage_ElbowShock:
                Handle_ElbowDmg(M, By);
                return;
            case SDamageType.SDamage_DropAttacked:
                Handle_DropAttackedDmg(By);
                return;
        }

        if (PreviousState == CNAME_s_PlayerWaiting || PreviousState == CNAME_s_ElbowShot_GoBackward || PreviousState == CNAME_s_PlayerChargingPrev)
        {
            // A spy goes back to the stance he has, with or without the pistol; a merc always armed.
            bool armed = pawn.SPawnType != PawnType.SPAWNOFFENSE || pawn.WeaponLoaded != 0;
            PreviousState = pawn.bIsCrouched
                ? (armed ? MovementStateNames.s_PlayerWalkingCr2H : MovementStateNames.s_PlayerWalkingCr0H)
                : (armed ? MovementStateNames.s_PlayerWalkingSt2H : MovementStateNames.s_PlayerWalkingSt0H);
        }

        eventMirrorState(PreviousState, Name.None, true);
    }

    /// <summary>
    /// The cam's fly-in hit: the owner's view is cleaned first; then the charge, elbow and drop reactions, a shock
    /// leaves the cam before the shock check, and any other hit with an animation leaves the cam.
    /// </summary>
    [Uc("SBase.SPlayerController.s_UseObject_StickyCam_SmoothToPosition.PawnTakeDamage", Sha1 = "1914b314c239ca2c5cdbd225621820be9b221043")]
    [PortOf("uc:SBase.SPlayerController:s_UseObject_StickyCam_SmoothToPosition.PawnTakeDamage", Spec = "spec/equipment/sticky-cam.md#using-a-cam")]
    private void StickyCamFlyInDamage(int Damage, Pawn? By, FVector Hit, FVector M, SDamageType T, int Pill, int Pct)
    {
        StandardReactionsToDamage(Damage, Pct, By, Hit, M, T, ref Pill);
        CleanStickyCamOnClient();
        if (Pawn is null)
        {
            return;
        }

        if (Pawn.Health <= 0)
        {
            CombatDeadDirectOrFatal(T, M);
            return;
        }

        switch (T)
        {
            case SDamageType.SDamage_Charged:
                Handle_ChargedDmg(M, By);
                return;
            case SDamageType.SDamage_ElbowShock:
                Handle_ElbowDmg(M, By);
                return;
            case SDamageType.SDamage_DropAttacked:
                eventMirrorState(CNAME_s_FallDropAttacked);
                return;
            case SDamageType.SDTStickyShocker:
                LeaveStickyCamView(false);
                eventCheckshock(Pill, false);
                return;
        }

        if (CombatIkOk && T != SDamageType.SDTBase_NoAnim)
        {
            LeaveStickyCamView(false);
        }
    }
}
