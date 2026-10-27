using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state GrabLedge: the spy caught a ledge in the air and the native moves him to the hang
// position while the grab clip plays. Written from spec/movement/ledge.md (GrabLedge).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.GrabLedge")]
    public class GrabLedge : UcActorState, IPlayerMove, ITopSynchro, INoGE
    {
        public static readonly GrabLedge Instance = new(UcMeta.GrabLedge, null);

        protected GrabLedge(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Provenance(ProvenanceKind.Decoded, "ledge.md GrabLedge step 1 and s_ChangeLedgeWait: camera smoothing 5.0")]
        public const float LedgeCameraSmoothValue = 5f;

        /// <summary>The hang camera, the ledge arms zone, standing, crouch latch off, repositioning not done.</summary>
        [Uc("SBase.SPlayerController.GrabLedge.BeginState", Sha1 = "f67d7afc5bedb8946e13f952104bbd7a6c23173e")]
        [PortOf("uc:SBase.SPlayerController:GrabLedge.BeginState", Spec = "spec/movement/ledge.md#grabledge-simulated")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.MovementCamera(ECamMode.ECM_OnLedge);
            pc.MovementCameraSmooth(LedgeCameraSmoothValue);
            pc.bTestBackFeetGrab = false;
            pc.SBPawn?.SetLedgeGrabZones();
            pc.ZoneHorsCrouch();
            pc.bCrouchWithRoll = false;
            pc.bCanStop = false;
        }

        /// <summary>The view inside the camera window; the pawn does not accelerate.</summary>
        [Uc("SBase.SPlayerController.GrabLedge.PlayerMove", Sha1 = "294d64147f8df3d9d8d201903bb6f5fbb3bba606")]
        [PortOf("uc:SBase.SPlayerController:GrabLedge.PlayerMove", Spec = "spec/movement/ledge.md#grabledge-simulated")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.LimitedViewByCamera(DeltaTime, 0, 0);
            if (self.Pawn is { } pawn)
            {
                pawn.Acceleration = default;
            }

            self.SendMove(DeltaTime, default);
        }

        /// <summary>The native reached the hang position.</summary>
        [Uc("SBase.SPlayerController.GrabLedge.TopSynchro", Sha1 = "9d1abf2a5b15ded959b82118865c797979d3913a")]
        [PortOf("uc:SBase.SPlayerController:GrabLedge.TopSynchro", Spec = "spec/movement/ledge.md#grabledge-simulated")]
        public virtual void TopSynchro(SPlayerController self)
        {
            if (self.IAmOnClient())
            {
                self.bCanStop = true;
            }
        }

        /// <summary>The grab clip ended: hanging.</summary>
        [Uc("SBase.SPlayerController.GrabLedge.AnimEnd", Sha1 = "2ef41257484284dc4151bc3d3a8f9417801873a2")]
        [PortOf("uc:SBase.SPlayerController:GrabLedge.AnimEnd", Spec = "spec/movement/ledge.md#grabledge-simulated")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_ONLEDGE);
            pc.eventChangeState(MovementStateNames.OnLedge);
        }

        /// <summary>The ledge vanished or was refused: fall from it (and do not catch it again at once).</summary>
        [Uc("SBase.SPlayerController.GrabLedge.NoGE", Sha1 = "19424536ab48917d333749da8b5b85d5f718897f")]
        [PortOf("uc:SBase.SPlayerController:GrabLedge.NoGE", Spec = "spec/movement/ledge.md#grabledge-simulated")]
        public virtual void NoGE(SPlayerController self) => self.LeaveLedgeFalling();
    }

    /// <summary>The ledge drop of GrabLedge.NoGE and OnLedge.NoGE: SA_LEDGEFALL, LastZipP = ZipP1, s_PlayerJump.</summary>
    [PortOf("uc:SBase.SPlayerController:OnLedge.NoGE", Spec = "spec/movement/ledge.md#hanging-onledge-simulated")]
    internal void LeaveLedgeFalling()
    {
        if (!IAmOnClient() || SBPawn is not { } pawn)
        {
            return;
        }

        pawn.ChangeEnumAction(SBasePawn.SA_LEDGEFALL);
        pawn.LastZipP = pawn.ZipP1;
        eventChangeState(MovementStateNames.s_PlayerJump);
    }
}
