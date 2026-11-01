using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_PlayerWalkingWeapon: weapon movement (the pawn faces the view and strafes), the
// merc's only walking family and the armed spy's. Written from spec/movement/walking.md (weapon walking).
//
// The source declares it as extending s_PlayerWalking; the generated state metadata lists no super state, so the
// state chain follows the metadata while the C# class keeps s_PlayerWalking's behaviour.
public partial class SPlayerController
{
    private static readonly Name WNAME_s_Reloading = new("s_Reloading");
    private static readonly Name WNAME_s_FullReloading = new("s_FullReloading");
    private static readonly Name WNAME_s_ElbowShocking = new("s_ElbowShocking");
    private static readonly Name WNAME_s_Reanimation = new("s_Reanimation");
    private static readonly Name WNAME_s_FireGrenade = new("s_FireGrenade");
    private static readonly Name WNAME_s_Charging = new("s_Charging");
    private static readonly Name WNAME_s_PlayerCharging = new("s_PlayerCharging");
    private static readonly Name WNAME_s_Berserk360 = new("s_Berserk360");

    /// <summary>
    /// The Use edge of weapon walking (melee-elbow-charge-berserk.md rule 1): the merc's charge with the stick pushed
    /// forward, else an interaction, a revive or finish, the merc's berserk spin or the elbow. True when a revive or
    /// finish started, where the move is sent at once and the rest of the frame is skipped.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon.PlayerMove", Spec = "spec/combat/melee-elbow-charge-berserk.md#who-presses-use-and-what-happens")]
    private bool UseEdgeWeapon(SBasePawn pawn, bool merc, float DeltaTime, FVector newAccel)
    {
        float now = Level.TimeSeconds;
        GameReplicationInfo? gri = GameReplicationInfo;
        bool chargeOn = gri is null || gri.bAvlb_MERC_Charge;
        float chargeStick = fPadXMaxLu * fChargeSeuilPad;
        bool interactions = IManager?.HasUsableInteractions() ?? false;
        if (merc && aForward >= chargeStick && !pawn.bIsCrouched &&
            (now - fLastChargingTime > iSecondBetweenTwoChargesWithoutEnnemy ||
             (now - fLastChargingTime > iSecondBetweenTwoChargesWithEnnemy && FindTargetToCharge())) &&
            CanChargeAfterBerserk && !(pawn.MyWeapon?.IsInState(WNAME_s_Reloading) ?? false) &&
            !interactions && chargeOn)
        {
            pawn.ChangeEnumAction(SBasePawn.SA_CHARGING);
            pawn.MyWeapon?.GotoState(WNAME_s_Charging);
            eventChangeState(WNAME_s_PlayerCharging);
            return false;
        }

        if (pawn.bStickyShocking || InteractionMenuRules.DoInteraction(this, eventCanInteract()))
        {
            return false;
        }

        if (FindPawnToReanimateOrTerminate())
        {
            SendMove(DeltaTime, newAccel);
            return true;
        }

        bool berserkOn = gri is null || gri.bAvlb_MERC_Berserk;
        if (merc && !pawn.bIsCrouched && (aForward < chargeStick || !chargeOn) && now - fLastBerserkTime > fSecondBetweenTwoBerserks &&
            now - fLastChargingTime > fSecondBetweenChargeAndBerserk && pawn.MyWeapon is { } rifle &&
            !rifle.IsInState(WNAME_s_Reloading) && !rifle.IsInState(WNAME_s_FullReloading) && !rifle.IsInState(WNAME_s_ElbowShocking) &&
            !rifle.IsInState(WNAME_s_Reanimation) && !rifle.IsInState(WNAME_s_FireGrenade) && !interactions && berserkOn)
        {
            pawn.ChangeEnumAction(SBasePawn.SA_BERSERK_ATTACK);
            fLastBerserkTime = now;
            eventChangeState(WNAME_s_Berserk360);
        }
        else
        {
            FindPawnToElbowShotAndGoForIt(true);
        }

        return false;
    }

    /// <remarks>
    /// The reload and rate-of-fire buttons are in <see cref="WeaponWalkingButtons"/> (merc-rifle.md). Not ported here:
    /// SwitchLaser and the disabled GadgetQuickChoice (equipment spec).
    /// </remarks>
    [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon")]
    public class s_PlayerWalkingWeapon : s_PlayerWalking, IPlayerMove, ICanCrouch, ITopLadder, INarrowLadder
    {
        public static new readonly s_PlayerWalkingWeapon Instance = new(UcMeta.s_PlayerWalkingWeapon, null);

        protected s_PlayerWalkingWeapon(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon.BeginState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon.BeginState", Spec = "spec/movement/walking.md#permission-predicates")]
        public override void BeginState(Actor self)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon.EndState", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon.EndState", Spec = "spec/movement/walking.md#permission-predicates")]
        public override void EndState(Actor self)
        {
        }

        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon.CanCrouch", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon.CanCrouch", Spec = "spec/movement/walking.md#weapon-walking-s_playerwalkingweaponplayermove")]
        public virtual bool CanCrouch(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon.CanActivateLaser", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon.CanActivateLaser", Spec = "spec/movement/walking.md#permission-predicates")]
        public override bool CanActivateLaser(SPlayerController self) => true;

        /// <summary>
        /// One frame of weapon walking: crouch requests, the weapon stick curve, the view and the pawn facing it, the
        /// ground speed with the merc's run and walk switch, the GE search, the merc's jump, then the move.
        /// </summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon.PlayerMove", Sha1 = "684d690df2ea24a12b65242e106174acb816221c")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon.PlayerMove", Spec = "spec/movement/walking.md#weapon-walking-s_playerwalkingweaponplayermove")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn || self.DeadInWalking(pawn))
            {
                return;
            }

            self.ProcessCrouch();
            self.ClampStick();
            self.WeaponStickScale(pawn);
            self.GroundPitch = 0;
            self.eventUpdateRotation(DeltaTime, 1f);
            FVector newAccel = self.UpdatePawnRotationWeapon(DeltaTime, self.ForwardRequest, self.StrafeRequest);
            WeaponGroundSpeed(pawn, ref newAccel);

            bool merc = pawn is SPAWNDEFENSE;
            if (merc)
            {
                if (pawn.fBlendCoeffWalkAnim != MovementK.MercRunBlendCoeff && pawn.GroundSpeed > MovementK.MercRunAbove)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_RUN_ST_2H);
                }

                if (pawn.fBlendCoeffWalkAnim == MovementK.MercRunBlendCoeff && pawn.GroundSpeed > 0f &&
                    pawn.GroundSpeed < MovementK.MercWalkBelow)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_2H);
                }
            }

            if (self.BT_UseJusteAppuye && self.UseEdgeWeapon(pawn, merc, DeltaTime, newAccel))
            {
                return;
            }

            self.WeaponWalkingButtons(pawn);
            pawn.ResetZones();
            self.bFindGE = false;
            self.eventCheckArmsZone();
            if (!self.bFindGE)
            {
                self.eventCheckFeetZone();
            }

            if (!merc)
            {
                self.bPressedJump = false;
            }

            if (self.bPressedJump && pawn.Base is not null && pawn.Base is not SBasePawn &&
                self.Level.TimeSeconds - self.fTimeOfLastLanding >= self.fTimeBetweenLandAndJump && pawn.CanUnCrouch())
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_UNCROUCH);
                pawn.MyDoJump();
                pawn.ChangeEnumAction(SBasePawn.SA_JUMP_ST_2H);
                self.bCrouchWithRoll = false;
                self.eventChangeState(MovementStateNames.s_PlayerJump);
            }

            self.bPressedJump = false;
            if (self.bFindGE)
            {
                newAccel = default;
            }

            self.SendMove(DeltaTime, newAccel);
        }

        /// <summary>A merc at a ladder top climbs down onto it.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon.TopLadder", Sha1 = "1abccae29d369b257a3810ebfcaa6ef9e476ca8c")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon.TopLadder", Spec = "spec/movement/walking.md#weapon-walking-s_playerwalkingweaponplayermove")]
        public virtual void TopLadder(SPlayerController self)
        {
            if (self.SBPawn is not SPAWNDEFENSE pawn || pawn.bWeaponTransit ||
                self.GetStateTable().Find(MovementStateNames.s_WaitAtTopLadder) is null)
            {
                return;
            }

            self.bFindGE = true;
            pawn.SendZipPAndNormalToServer();
            pawn.SetInfoForVandamme(pawn.m_geoNormal);
            pawn.ChangeEnumAction(SBasePawn.SA_FAST_REPOS);
            self.eventChangeState(MovementStateNames.s_WaitAtTopLadder);
        }

        /// <summary>A merc at a ladder foot grabs it.</summary>
        [Uc("SBase.SPlayerController.s_PlayerWalkingWeapon.NarrowLadder", Sha1 = "b4245b51e83f904647415a5874c746e45203198a")]
        [PortOf("uc:SBase.SPlayerController:s_PlayerWalkingWeapon.NarrowLadder", Spec = "spec/movement/walking.md#weapon-walking-s_playerwalkingweaponplayermove")]
        public virtual void NarrowLadder(SPlayerController self)
        {
            if (self.SBPawn is not SPAWNDEFENSE pawn || pawn.bWeaponTransit ||
                self.GetStateTable().Find(MovementStateNames.s_GrabLadder) is null)
            {
                return;
            }

            self.bFindGE = true;
            pawn.ChangeEnumAction(SBasePawn.SA_LADDER_GRABW);
            pawn.SendZipPAndNormalToServer();
            self.eventChangeState(MovementStateNames.s_GrabLadder);
        }
    }
}
