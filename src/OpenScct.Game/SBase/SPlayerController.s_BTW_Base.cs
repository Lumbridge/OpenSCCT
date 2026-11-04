using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_BTW_Base: the common part of the back-to-wall states. Written from
// spec/movement/back-to-wall.md (s_BTW_Base). PawnTakeDamage is the combat port's (DamageBackToWall); PlayAnimDmg
// leaves the wall (BtwPlayAnimDmg in SPlayerController.s_BTWFire.cs).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_BTW_Base")]
    public class s_BTW_Base : UcActorState, IPlayerMove, ICanUseCam, IPlayAnimDmg
    {
        public static readonly s_BTW_Base Instance = new(UcMeta.s_BTW_Base, null);

        protected s_BTW_Base(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_BTW_Base.BeginState", Sha1 = "bb5f3ad3068ff2065d42a0d3873d90df14925604")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_Base.BeginState", Spec = "spec/movement/back-to-wall.md#s_btw_base-common")]
        public override void BeginState(Actor self) => ReinitIfTazered((SPlayerController)self);

        /// <summary>The camera turns within the wall view; the spy does not move.</summary>
        [Uc("SBase.SPlayerController.s_BTW_Base.PlayerMove", Sha1 = "11a875f58b3fc1db0f049c2404e13d981be2ee5e")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_Base.PlayerMove", Spec = "spec/movement/back-to-wall.md#s_btw_base-common")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateBtwRotation(DeltaTime);
            self.SendMove(DeltaTime, default);
        }

        /// <summary>A hit leaves the wall to the resting state, keeping a drawn pistol.</summary>
        [Uc("SBase.SPlayerController.s_BTW_Base.PlayAnimDmg", Sha1 = "d477481c48ce6a0ed6b57f36f0ef9e4d56180429")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_Base.PlayAnimDmg", Spec = "spec/movement/back-to-wall.md#s_btw_base-common")]
        public virtual void PlayAnimDmg(SPlayerController self, int PillTag, FVector Momentum) => self.BtwPlayAnimDmg(bKeepWeapon: true);

        [Uc("SBase.SPlayerController.s_BTW_Base.CanUseCam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_Base.CanUseCam", Spec = "spec/movement/back-to-wall.md#s_btw_base-common")]
        public virtual bool CanUseCam(SPlayerController self) => true;

        /// <summary>
        /// A tazered spy (controlling side) leaves the wall: facing along the wall normal, SA_BTW_OUT, and the
        /// resting state for the stance and hands with its action.
        /// </summary>
        [Uc("SBase.SPlayerController.s_BTW_Base.ReinitIfTazered", Sha1 = "280dc810c7907334e00328972d793cd1fbe488b7")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_Base.ReinitIfTazered", Spec = "spec/movement/back-to-wall.md#s_btw_base-common")]
        public virtual void ReinitIfTazered(SPlayerController self)
        {
            if (!self.IAmOnClient() || !self.bTazered || self.SBPawn is not { } pawn)
            {
                return;
            }

            self.FaceWall(pawn);
            pawn.ChangeEnumAction(SBasePawn.SA_BTW_OUT);
            self.LeaveWallToRest(pawn);
        }
    }

    /// <summary>The wall view's inward/outward side, retained across stances and restored by prediction.</summary>
    public int BtwViewYawOffset { get; set; }

    // Reset only on a genuine wall entry or return from a peek. Resetting while merely holding/changing stance
    // would let repeated mouse turns move the window itself and permit a full orbit again.
    internal void BeginBtwView()
    {
        int wallYaw = !vWallNormal.IsZero() ? vWallNormal.Rotation().Yaw : SBPawn?.Rotation.Yaw ?? Rotation.Yaw;
        BtwViewYawOffset = System.Math.Abs(FRotator.NormalizeAxis(Rotation.Yaw - wallYaw)) > 16384 ? 32768 : 0;
    }

    /// <summary>The current wall view's limits and reference, shared with render-time mouse prediction.</summary>
    public bool TryGetBtwViewLimits(out CameraWindow window, out int yawReference)
    {
        window = default;
        yawReference = 0;
        if (SBPawn is not { } pawn)
        {
            return false;
        }

        bool peeking = true;
        if (StateNode is s_BTWFireStandLeft or s_BTWFireCrouchLeft)
        {
            window = new(MovementK.BtwFireLeftYawMin, MovementK.BtwFireLeftYawMax, -MovementK.BtwFirePitch, MovementK.BtwFirePitch);
        }
        else if (StateNode is s_BTWFireStandRight or s_BTWFireCrouchRight)
        {
            window = new(MovementK.BtwFireRightYawMin, MovementK.BtwFireRightYawMax, -MovementK.BtwFirePitch, MovementK.BtwFirePitch);
        }
        else if (StateNode is s_BTW_StandLeft or s_BTW_StandRight or s_BTW_CrouchLeft or s_BTW_CrouchRight)
        {
            int view = MovementK.BtwPeekView;
            window = new(-view, view, -view, view);
        }
        else if (StateNode is s_BTW or s_EnterBTW or s_BTWCorner)
        {
            window = CameraWindows.WindowOf(ECamMode.ECM_BTW);
            peeking = false;
        }
        else
        {
            return false;
        }

        // Preserve which side the player viewed the wall from on entry. Forcing a wall-in-front entry's camera
        // around to the pawn's new outward facing also changes the camera-relative sidestep input.
        int wallYaw = StateNode is s_EnterBTW && !vWallNormal.IsZero() ? vWallNormal.Rotation().Yaw : pawn.Rotation.Yaw;
        yawReference = wallYaw + (peeking ? 32768 : BtwViewYawOffset);
        return true;
    }

    internal void UpdateBtwRotation(float deltaTime)
    {
        if (TryGetBtwViewLimits(out CameraWindow window, out int yawReference))
        {
            UpdateLimitedRotation(deltaTime, window.MinYaw, window.MaxYaw, window.MinPitch, window.MaxPitch, yawReference, 0);
        }
    }

    /// <summary>The pawn faces along vWallNormal (its back to the wall), pitch and roll 0.</summary>
    [PortOf("uc:SBase.SPlayerController:s_BTW.BeginState", Spec = "spec/movement/back-to-wall.md#on-the-wall-s_btw_up-standing-s_btw_down-crouched-both-extend-s_btw")]
    internal void FaceWall(SBasePawn pawn)
    {
        if (!vWallNormal.IsZero())
        {
            pawn.Rotation = new FRotator(0, vWallNormal.Rotation().Yaw, 0);
        }
    }

    /// <summary>Off the wall to the resting state for the stance and hands, each with its action.</summary>
    internal void LeaveWallToRest(SBasePawn pawn)
    {
        bool armed = pawn.WeaponLoaded != 0;
        if (pawn.bIsCrouched)
        {
            pawn.ChangeEnumAction(armed ? SBasePawn.SA_WALK_CR_2H : SBasePawn.SA_WALK_CR_0H);
            eventChangeState(armed ? MovementStateNames.s_PlayerWalkingCr2H : MovementStateNames.s_PlayerWalkingCr0H);
        }
        else
        {
            pawn.ChangeEnumAction(armed ? SBasePawn.SA_WALK_ST_2H : SBasePawn.SA_WALK_ST_0H);
            eventChangeState(armed ? MovementStateNames.s_PlayerWalkingSt2H : MovementStateNames.s_PlayerWalkingSt0H);
        }
    }
}
