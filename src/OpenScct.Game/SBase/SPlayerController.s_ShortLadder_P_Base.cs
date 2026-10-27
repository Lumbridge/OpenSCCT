using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ShortLadder_P_Base: the crouched boost helper of a short ladder. Written from
// spec/movement/coop.md (short ladder, helper).
//
// The source declares it as extending s_PlayerWalkingCr0H; the generated state metadata lists no super state, so
// the state chain follows the metadata while the C# class keeps s_PlayerWalkingCr0H's behaviour.
public partial class SPlayerController
{
    /// <remarks>PawnTakeDamage is the combat port's (by state name); the ignored inputs come from the metadata.</remarks>
    [Uc("SBase.SPlayerController.s_ShortLadder_P_Base")]
    public class s_ShortLadder_P_Base : s_PlayerWalkingCr0H, IClientDamage
    {
        public static new readonly s_ShortLadder_P_Base Instance = new(UcMeta.s_ShortLadder_P_Base, null);

        protected s_ShortLadder_P_Base(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Hit feedback, the stop (forced), and a living helper goes back to walking.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_Base.ClientDamage", Sha1 = "be4d688d4546dfa3e6f525f97d4141d4b413abe0")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Base.ClientDamage", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public virtual void ClientDamage(SPlayerController self, FVector Momentum, SDamageType DamageType, FVector HitLocation, Pawn? instigatedBy)
        {
            self.GlobalClientDamage(Momentum, DamageType, HitLocation, instigatedBy);
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_SHL_P_STOP, true);
            if (pawn.Health > 0)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_SHL_P_STOP, true);
                self.GotoWaitingState();
            }
        }

        [Uc("SBase.SPlayerController.s_ShortLadder_P_Base.CanUseCam", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Base.CanUseCam", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override bool CanUseCam(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_ShortLadder_P_Base.CanSwitchGoggleManually", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Base.CanSwitchGoggleManually", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override bool CanSwitchGoggleManually(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_ShortLadder_P_Base.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Base.CanActivateLaser", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override bool CanActivateLaser(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_ShortLadder_P_Base.CanShortLadderHelp", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Base.CanShortLadderHelp", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override bool CanShortLadderHelp(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_ShortLadder_P_Base.CanIUseBinocular", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Base.CanIUseBinocular", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override bool CanIUseBinocular(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_ShortLadder_P_Base.CanUncrouchAndUndraw", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Base.CanUncrouchAndUndraw", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override bool CanUncrouchAndUndraw(SPlayerController self) => true;

        /// <summary>No fire mode, crouch or roll while helping; then the crouched walking tick.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_Base.PlayerTick", Sha1 = "853e0a872e71242ae41f68eeb44c20c8cdcebc37")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Base.PlayerTick", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.bFireMode = 0;
            pc.bCrouch = 0;
            pc.bRoll = 0;
            base.PlayerTick(self, DeltaTime);
        }

        /// <summary>Held still, the view turning, no jump.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_P_Base.PlayerMove", Sha1 = "8eaf67c6f482f7035721c00418504be65c3e3652")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_P_Base.PlayerMove", Spec = "spec/movement/coop.md#short-ladder-helper")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateCamRotation(DeltaTime);
            self.bPressedJump = false;
            self.SendMove(DeltaTime, default);
        }
    }
}
