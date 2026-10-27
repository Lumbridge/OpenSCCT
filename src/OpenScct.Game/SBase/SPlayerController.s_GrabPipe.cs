using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_GrabPipe: the grab clip onto a vertical pipe (from the ground, the air or a
// ledge). Written from spec/movement/pipe-vertical.md (s_GrabPipe).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_GrabPipe")]
    public class s_GrabPipe : UcActorState, IPlayerMove, IHandOverHand
    {
        public static readonly s_GrabPipe Instance = new(UcMeta.s_GrabPipe, null);

        protected s_GrabPipe(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Stand up, the view inside the camera window around the pawn, no acceleration.</summary>
        [Uc("SBase.SPlayerController.s_GrabPipe.PlayerMove", Sha1 = "b6bf3b19ed3f88ebfd55c33277e84edbd5b3d545")]
        [PortOf("uc:SBase.SPlayerController:s_GrabPipe.PlayerMove", Spec = "spec/movement/pipe-vertical.md#s_grabpipe-ignores-bump-seeplayer-hearnoise-binoculars-disabled")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            self.bCrouchWithRoll = false;
            self.ZoneHorsCrouch();
            if (self.Pawn is { } pawn)
            {
                self.LimitedViewByCamera(DeltaTime, pawn.Rotation.Yaw, pawn.Rotation.Pitch);
            }

            self.SendMove(DeltaTime, default);
        }

        /// <summary>The grab clip ended: climbing.</summary>
        [Uc("SBase.SPlayerController.s_GrabPipe.AnimEnd", Sha1 = "78712abadf50802af13c6c4735df1bf8f5fbbba4")]
        [PortOf("uc:SBase.SPlayerController:s_GrabPipe.AnimEnd", Spec = "spec/movement/pipe-vertical.md#s_grabpipe-ignores-bump-seeplayer-hearnoise-binoculars-disabled")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_ON_PIPE);
            pc.eventChangeState(MovementStateNames.s_OnPipe);
        }

        [Uc("SBase.SPlayerController.s_GrabPipe.EndState", Sha1 = "2a0b741602c58395b1c79ef18b417548327b41cf")]
        [PortOf("uc:SBase.SPlayerController:s_GrabPipe.EndState", Spec = "spec/movement/pipe-vertical.md#s_grabpipe-ignores-bump-seeplayer-hearnoise-binoculars-disabled")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
        }

        /// <summary>A hand-over-hand bar at the pipe: the turn onto it (pipe-horizontal.md).</summary>
        [Uc("SBase.SPlayerController.s_GrabPipe.HandOverHand", Sha1 = "47ef15aad200c653776ff053ed6cf72a72d3c2ed")]
        [PortOf("uc:SBase.SPlayerController:s_GrabPipe.HandOverHand", Spec = "spec/movement/pipe-vertical.md#to-hand-over-hand-s_tr_pipev2h_oriente--_orientedown-s_tr_pipev2h")]
        public virtual void HandOverHand(SPlayerController self) => self.PipeToHandOverHand();
    }

    /// <summary>
    /// From a vertical pipe to a hand-over-hand bar (controlling side): camera, the bar's points to the server and the
    /// orient state up or down, when the hand-over-hand states are ported.
    /// </summary>
    [PortOf("uc:SBase.SPlayerController:s_OnPipe.HandOverHand", Spec = "spec/movement/pipe-vertical.md#to-hand-over-hand-s_tr_pipev2h_oriente--_orientedown-s_tr_pipev2h")]
    internal void PipeToHandOverHand()
    {
        if (!IAmOnClient() || SBPawn is not { } pawn)
        {
            return;
        }

        Name next = pawn.NewZipP1.Z > pawn.Location.Z ? MovementStateNames.s_Tr_PipeV2H_Oriente : MovementStateNames.s_Tr_PipeV2H_OrienteDown;
        if (GetStateTable().Find(next) is null)
        {
            return;
        }

        MovementCamera(ECamMode.ECM_NoTargetCol);
        bFindGE = true;
        pawn.SendNewZipPToServer();
        eventChangeState(next);
    }
}
