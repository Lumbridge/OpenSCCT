using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ZipLine: sliding down a zip line in the normal pose; the parent of the speed and
// slow poses. Written from spec/movement/zip-line.md (Sliding).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ZipLine")]
    public class s_ZipLine : UcActorState, IPlayerMove, INoGE, INotifyHitWall, IDropAttack, ICanBeDropAttacked
    {
        public static readonly s_ZipLine Instance = new(UcMeta.s_ZipLine, null);

        protected s_ZipLine(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_ZipLine.BeginState", Sha1 = "91e42eb6a909ae51602346ccf6d2c223eec0b99d")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLine.BeginState", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
        public override void BeginState(Actor self)
        {
            ((SPlayerController)self).MovementCamera(ECamMode.ECM_FSphere);
            base.BeginState(self);
        }

        /// <summary>The end wall, the speed pose (stick forward), the slow pose (stick back) or a drop; then the slide.</summary>
        [Uc("SBase.SPlayerController.s_ZipLine.PlayerMove", Sha1 = "cba3dd3c8a424101cf846cba7e57e80657ab0d5b")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLine.PlayerMove", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.UpdateCamRotation(DeltaTime);
            if (self.TestZipWall())
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ZIP_WALL);
                self.eventChangeState(MovementStateNames.s_ZipWall);
            }
            else if (self.aForward > MovementK.ZipSpeedIn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_ZIP_SPEED);
                self.eventChangeState(MovementStateNames.s_ZipLineSpeedTR);
            }
            else if (self.aForward < -MovementK.ZipSpeedIn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_ZIP_SLOW);
                self.eventChangeState(MovementStateNames.s_ZipLineSlowTR);
            }
            else if (self.bCrouchWithRoll)
            {
                self.DropFromZip(pawn);
            }

            self.SendMove(DeltaTime, self.ZipSlide(pawn));
        }

        /// <summary>The slide loop restarts when the double-jump channel's clip ends.</summary>
        [Uc("SBase.SPlayerController.s_ZipLine.AnimEnd", Sha1 = "3555fcdb5ad628aab4f6694888b1ede5431b3379")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLine.AnimEnd", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == MovementK.DoubleJumpChannel && pc.IAmOnClient())
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_ON_ZIP);
            }
        }

        /// <summary>The end of the line: drop.</summary>
        [Uc("SBase.SPlayerController.s_ZipLine.NoGE", Sha1 = "dec696b1291489adb391afb0bc8c5c42bf1b31e3")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLine.NoGE", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
        public virtual void NoGE(SPlayerController self)
        {
            if (self.SBPawn is { } pawn)
            {
                self.DropFromZip(pawn);
            }
        }

        /// <summary>A wall at the low end: fall from the line.</summary>
        [Uc("SBase.SPlayerController.s_ZipLine.NotifyHitWall", Sha1 = "0d3915798c68965290ec5833cb70971f34cd65d7")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLine.NotifyHitWall", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
        public virtual bool NotifyHitWall(SPlayerController self, FVector HitNormal, Actor? Wall)
        {
            if (self.IAmOnClient() && self.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ZIPFALL);
                self.eventChangeState(MovementStateNames.s_PlayerJump);
            }

            return true;
        }

        /// <summary>Sliding into a pawn: the drop-attack knock (server) or the landing state and the request (client).</summary>
        [Uc("SBase.SPlayerController.s_ZipLine.DropAttack", Sha1 = "41d7e737589c480ca4c475056adfe28dceb726ed")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLine.DropAttack", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
        public virtual void DropAttack(SPlayerController self, SBasePawn PawnToAttack)
        {
            IMovementHooks hooks = MovementService.HooksOf(self);
            if (self.Role == ENetRole.ROLE_Authority)
            {
                hooks.PerformDropAttack(self, PawnToAttack);
            }
            else if (self.ChangeStateIfPorted(MovementStateNames.s_DropAttack_Landing))
            {
                hooks.TellServerForDropAttack(self, PawnToAttack);
            }
        }

        [Uc("SBase.SPlayerController.s_ZipLine.CanBeDropAttacked", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLine.CanBeDropAttacked", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
        public virtual bool CanBeDropAttacked(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_ZipLine.EndState", Sha1 = "2a0b741602c58395b1c79ef18b417548327b41cf")]
        [PortOf("uc:SBase.SPlayerController:s_ZipLine.EndState", Spec = "spec/movement/zip-line.md#sliding-s_zipline-and-the-speed--slow-children")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
        }
    }
}
