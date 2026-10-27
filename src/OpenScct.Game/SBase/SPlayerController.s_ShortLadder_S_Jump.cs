using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ShortLadder_S_Jump: the boost climber jumps from the helper's hands; the end of the
// boost clip is a ledge grab (which drops him through NoGE when no ledge is in reach). Written from
// spec/movement/coop.md (short ladder, climber, rule 4).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ShortLadder_S_Jump")]
    public class s_ShortLadder_S_Jump : s_ShortLadder_S_Base, IPlayerMove, IClientStickyShock
    {
        public static new readonly s_ShortLadder_S_Jump Instance = new(UcMeta.s_ShortLadder_S_Jump, s_ShortLadder_S_Base.Instance);

        protected s_ShortLadder_S_Jump(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>A sticky shock drops the climber into a jump.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_S_Jump.ClientStickyShock", Sha1 = "a38d9568c35a383c80ea30e42f34533108a1a1bc")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_S_Jump.ClientStickyShock", Spec = "spec/movement/coop.md#short-ladder-climber")]
        public virtual void ClientStickyShock(SPlayerController self, int PillTag, bool bPlayAnim)
        {
            self.GlobalClientStickyShock(PillTag, bPlayAnim);
            self.SBPawn?.ChangeEnumAction(SBasePawn.SA_JUMP_ST_0H);
            self.eventChangeState(MovementStateNames.s_PlayerJump);
        }

        [Uc("SBase.SPlayerController.s_ShortLadder_S_Jump.PlayerMove", Sha1 = "ecda6b0168c93e6432d2e1b4f528926ec2b8d124")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_S_Jump.PlayerMove", Spec = "spec/movement/coop.md#short-ladder-climber")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            self.UpdateCamRotation(DeltaTime);
            self.SendMove(DeltaTime, default);
        }

        /// <summary>The controlling side: the centred smooth camera and the boost clip.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_S_Jump.BeginState", Sha1 = "653b02bfcebbf6bef8796054818228e18b7620e1")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_S_Jump.BeginState", Spec = "spec/movement/coop.md#short-ladder-climber")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient())
            {
                pc.MovementCamera(ECamMode.ECM_SmoothTransitionCentered);
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_SHL_S_JUMP);
            }
        }

        /// <summary>The boost clip ended (controlling side): the ledge grab.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_S_Jump.AnimEnd", Sha1 = "0a7a8e1c419d9b37527f1c40db9cb51c928d9826")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_S_Jump.AnimEnd", Spec = "spec/movement/coop.md#short-ladder-climber")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient())
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_LEDGE_GRAB);
                pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
                pc.bFindGE = true;
                pc.eventChangeState(MovementStateNames.GrabLedge);
            }
            else
            {
                base.AnimEnd(self, Channel);
            }
        }
    }
}
