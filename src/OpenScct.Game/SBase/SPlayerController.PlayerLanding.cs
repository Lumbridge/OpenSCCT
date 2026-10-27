using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state PlayerLanding: the landing clip after a fall. A low landing can be walked out of, a
// high one holds the player for its clip. Written from spec/movement/jump-fall-land.md (landing states).
//
// The source declares it as extending s_PlayerWalkingMario; the generated state metadata lists no super state, so
// the state chain follows the metadata while the C# class keeps the Mario behaviour.
public partial class SPlayerController
{
    /// <remarks>Not ported here: the ignored GadgetQuickChoice, PlayAnimDmg and SwitchBinocularVision (other areas).</remarks>
    [Uc("SBase.SPlayerController.PlayerLanding")]
    public class PlayerLanding : s_PlayerWalkingMario
    {
        public static new readonly PlayerLanding Instance = new(UcMeta.PlayerLanding, null);

        private readonly int _dropAttackPc;

        protected PlayerLanding(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
            foreach ((string name, int pc) in info.LabelPcs)
            {
                if (name == "DropAttack")
                {
                    _dropAttackPc = pc;
                }
            }
        }

        /// <summary>No back-ledge turn; the merc's weapon lands; an unarmed standing spy makes a landing noise.</summary>
        [Uc("SBase.SPlayerController.PlayerLanding.BeginState", Sha1 = "6e5c1b5c8fd791114344a659b33c82db197c08ca")]
        [PortOf("uc:SBase.SPlayerController:PlayerLanding.BeginState", Spec = "spec/movement/jump-fall-land.md#landing-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.bTestBackFeetGrab = false;
            pc.LandingWeaponClip();
            if (pc.SBPawn is SPawnAttaque { WeaponLoaded: 0, bIsCrouched: false } pawn)
            {
                pawn.NotifyNoise(MovementK.LandingNoise);
            }

            base.BeginState(self);
        }

        /// <summary>
        /// No jump; moving the stick out of a low landing ends it at once; the view turns (the merc keeps facing it)
        /// and the pawn gets no acceleration.
        /// </summary>
        [Uc("SBase.SPlayerController.PlayerLanding.PlayerMove", Sha1 = "91513bcf457be059e366ec540e795ea62524e79e")]
        [PortOf("uc:SBase.SPlayerController:PlayerLanding.PlayerMove", Spec = "spec/movement/jump-fall-land.md#landing-states")]
        public override void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.bPressedJump = false;
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            if (pawn.GetAnimName(0) != MovementStateNames.LandStAlAk0)
            {
                float sq = MovementK.StickDeadSquare;
                bool stick = !(self.aForward > -sq && self.aForward < sq && self.aStrafe > -sq && self.aStrafe < sq);
                bool high = pawn.sAction is SBasePawn.SA_LAND_ST_0H_HIGH or SBasePawn.SA_LAND_ST_0HTo2H_HIGH or SBasePawn.SA_LAND_ST_2H_HIGH;
                if (stick && !high)
                {
                    if (pawn.PawnAnim[0].Frame < 0f)
                    {
                        pawn.PlayLandingSound();
                    }

                    self.GotoWaitingState();
                }
            }

            self.eventUpdateRotation(DeltaTime, 1f);
            if (pawn is SPAWNDEFENSE)
            {
                pawn.Rotation = new FRotator(0, self.Rotation.Yaw, 0);
            }

            self.SendMove(DeltaTime, default);
        }

        /// <summary>The landing clip ended: the resting state (a sticky shock in progress plays its clip again).</summary>
        [Uc("SBase.SPlayerController.PlayerLanding.AnimEnd", Sha1 = "ad7bb00d2ad851c209327963bda49f86e880f74c")]
        [PortOf("uc:SBase.SPlayerController:PlayerLanding.AnimEnd", Spec = "spec/movement/jump-fall-land.md#landing-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient())
            {
                return;
            }

            pc.GotoWaitingState();
            if (pc.SBPawn is { bStickyShocking: true } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_DMG_ELECTRIC, true);
            }
        }

        /// <summary>The label DropAttack (a drop-attack landing): the drop-attack landing clip, then nothing.</summary>
        [Uc("SBase.SPlayerController.PlayerLanding@code", Sha1 = "68c67538af83d574e21665fdcf2a3ad8c697ca56")]
        [PortOf("uc:SBase.SPlayerController:PlayerLanding@code", Spec = "spec/movement/jump-fall-land.md#landing-states")]
        public override void Code(Actor self, ref LatentFrame frame)
        {
            var pc = (SPlayerController)self;
            if (frame.Pc == _dropAttackPc && _dropAttackPc > 0 && pc.IAmOnClient())
            {
                pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_LAND_DROPATTACK);
            }

            frame.Pc = LatentFrame.NoCode;
        }

        [Uc("SBase.SPlayerController.PlayerLanding.NotifyHitWall", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:PlayerLanding.NotifyHitWall", Spec = "spec/movement/jump-fall-land.md#landing-states")]
        public override bool NotifyHitWall(SPlayerController self, FVector HitNormal, Actor? Wall) => false;

        /// <summary>Ignored while landing.</summary>
        public override void AutoFeetLedge(SPlayerController self)
        {
        }

        /// <summary>Ignored while landing.</summary>
        public override void NarrowLadder(SPlayerController self)
        {
        }

        /// <summary>Ignored while landing.</summary>
        public override void Pipe(SPlayerController self, FVector locationEnd, FRotator orientationEnd)
        {
        }

        /// <summary>Ignored while landing.</summary>
        public override void TopLadder(SPlayerController self)
        {
        }

        /// <summary>Ignored while landing.</summary>
        public override void TopPipe(SPlayerController self, bool bLeft)
        {
        }
    }

    /// <summary>The armed merc's weapon plays its landing clip (equipment spec).</summary>
    private void LandingWeaponClip()
    {
        if (SBPawn is SPAWNDEFENSE { WeaponLoaded: > 0 })
        {
            MovementService.HooksOf(this).PlayWeaponClip(this, MovementWeaponClip.Land);
        }
    }
}
