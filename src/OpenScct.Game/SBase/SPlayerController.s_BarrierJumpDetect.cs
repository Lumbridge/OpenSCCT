using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController barrier and low ledge states: the detect states (the ledge snap before the move:
// s_BarrierJumpDetect, s_FeetLedgeDetect, s_WaistLedgeDetect) and the climbs (s_BarrierJump, s_FeetLedge,
// s_WaistLedge, s_WaistLedge_Cr). Written from spec/movement/barrier-and-low-ledges.md.
public partial class SPlayerController
{
    /// <summary>The low ledge was lost during the snap: fall, and the same ledge is not caught again.</summary>
    internal void LowLedgeLost()
    {
        if (!IAmOnClient() || SBPawn is not { } pawn)
        {
            return;
        }

        pawn.ChangeEnumAction(SBasePawn.SA_LEDGEFALL);
        pawn.LastZipP = pawn.ZipP1;
        eventChangeState(MovementStateNames.s_PlayerJump);
    }

    /// <summary>The waist climb decision once both the grab clip and the snap are done: standing or crouched.</summary>
    internal void WaistLedgeStep()
    {
        if (!IAmOnClient() || SBPawn is not { } pawn)
        {
            return;
        }

        // AnimEnd marks the grab complete; only a live TopSynchro starts the climb. A settled hold
        // reports that position signal every tick, so it cannot stand in for an unfinished grab.
        // Starting here also keeps the root-motion action in the physics phase during prediction/replay.
        if (!bCanStop || pawn.PawnAnim[0].bPlaying)
        {
            return;
        }

        if (!bWaistLedgeCanGoUnCrouch || bCrouchWithRoll)
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WAISTLEDGE_CR);
            eventChangeState(MovementStateNames.s_WaistLedge_Cr);
        }
        else
        {
            pawn.ChangeEnumAction(SBasePawn.SA_WAISTLEDGE);
            eventChangeState(MovementStateNames.s_WaistLedge);
        }
    }

    /// <summary>The camera view with no move (the detect and climb states).</summary>
    internal void CamViewNoMove(float DeltaTime)
    {
        UpdateCamRotation(DeltaTime);
        SendMove(DeltaTime, default);
    }

    [Uc("SBase.SPlayerController.s_BarrierJumpDetect")]
    public class s_BarrierJumpDetect : UcActorState, IPlayerMove, ITopSynchro, INoGE
    {
        public static readonly s_BarrierJumpDetect Instance = new(UcMeta.s_BarrierJumpDetect, null);

        protected s_BarrierJumpDetect(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>In place at the rail: the vault.</summary>
        [Uc("SBase.SPlayerController.s_BarrierJumpDetect.TopSynchro", Sha1 = "7d935c08258fc5e885a6ee45d8660c35f6e700a5")]
        [PortOf("uc:SBase.SPlayerController:s_BarrierJumpDetect.TopSynchro", Spec = "spec/movement/barrier-and-low-ledges.md#detect-states-repositioning")]
        public virtual void TopSynchro(SPlayerController self)
        {
            if (self.IAmOnClient() && self.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_BARRIER_JUMP);
                self.eventChangeState(MovementStateNames.s_BarrierJump);
            }
        }

        [Uc("SBase.SPlayerController.s_BarrierJumpDetect.NoGE", Sha1 = "19424536ab48917d333749da8b5b85d5f718897f")]
        [PortOf("uc:SBase.SPlayerController:s_BarrierJumpDetect.NoGE", Spec = "spec/movement/barrier-and-low-ledges.md#detect-states-repositioning")]
        public virtual void NoGE(SPlayerController self) => self.LowLedgeLost();

        [Uc("SBase.SPlayerController.s_BarrierJumpDetect.PlayerMove", Sha1 = "235628e912701e62b662b980fc8891d73915caab")]
        [PortOf("uc:SBase.SPlayerController:s_BarrierJumpDetect.PlayerMove", Spec = "spec/movement/barrier-and-low-ledges.md#detect-states-repositioning")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateCamRotation(DeltaTime);
            self.bPressedJump = false;
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_BarrierJump")]
    public class s_BarrierJump : UcActorState, IPlayerMove, ICheckArmsZone, IMyFalling
    {
        public static readonly s_BarrierJump Instance = new(UcMeta.s_BarrierJump, null);

        protected s_BarrierJump(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>No grab during the vault (ignored).</summary>
        public virtual void CheckArmsZone(SPlayerController self)
        {
        }

        /// <summary>No fall during the vault (ignored).</summary>
        public virtual void MyFalling(SPlayerController self, float LeftTime)
        {
        }

        /// <summary>Over the rail: falling on the far side.</summary>
        [Uc("SBase.SPlayerController.s_BarrierJump.AnimEnd", Sha1 = "c0abea6f053b234df4c84ad8363f845f87982ba5")]
        [PortOf("uc:SBase.SPlayerController:s_BarrierJump.AnimEnd", Spec = "spec/movement/barrier-and-low-ledges.md#climb-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pc.MovementCameraSmooth(CameraSmoothDefault);
                pawn.ChangeEnumAction(SBasePawn.SA_FALL_AFTERBARRIER);
                pc.eventChangeState(MovementStateNames.s_PlayerJump);
            }
        }

        [Uc("SBase.SPlayerController.s_BarrierJump.PlayerMove", Sha1 = "4031068f6d66f5ef1e89a306b006fa4cc4e981c3")]
        [PortOf("uc:SBase.SPlayerController:s_BarrierJump.PlayerMove", Spec = "spec/movement/barrier-and-low-ledges.md#climb-states")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateCamRotation(DeltaTime);
            self.bPressedJump = false;
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_FeetLedgeDetect")]
    public class s_FeetLedgeDetect : UcActorState, IPlayerMove, ITopSynchro, INoGE
    {
        public static readonly s_FeetLedgeDetect Instance = new(UcMeta.s_FeetLedgeDetect, null);

        protected s_FeetLedgeDetect(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The feet at the ledge: the step up.</summary>
        [Uc("SBase.SPlayerController.s_FeetLedgeDetect.TopSynchro", Sha1 = "3db4b47aa0ae9878e657d452be87dca5340d93cb")]
        [PortOf("uc:SBase.SPlayerController:s_FeetLedgeDetect.TopSynchro", Spec = "spec/movement/barrier-and-low-ledges.md#detect-states-repositioning")]
        public virtual void TopSynchro(SPlayerController self)
        {
            if (self.IAmOnClient() && self.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_FEETLEDGE);
                self.eventChangeState(MovementStateNames.s_FeetLedge);
            }
        }

        [Uc("SBase.SPlayerController.s_FeetLedgeDetect.NoGE", Sha1 = "19424536ab48917d333749da8b5b85d5f718897f")]
        [PortOf("uc:SBase.SPlayerController:s_FeetLedgeDetect.NoGE", Spec = "spec/movement/barrier-and-low-ledges.md#detect-states-repositioning")]
        public virtual void NoGE(SPlayerController self) => self.LowLedgeLost();

        [Uc("SBase.SPlayerController.s_FeetLedgeDetect.PlayerMove", Sha1 = "69a0d6ae2c737e7f2cb64552735cce49bdf9d134")]
        [PortOf("uc:SBase.SPlayerController:s_FeetLedgeDetect.PlayerMove", Spec = "spec/movement/barrier-and-low-ledges.md#detect-states-repositioning")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime) => self.CamViewNoMove(DeltaTime);
    }

    [Uc("SBase.SPlayerController.s_FeetLedge")]
    public class s_FeetLedge : UcActorState, IPlayerMove
    {
        public static readonly s_FeetLedge Instance = new(UcMeta.s_FeetLedge, null);

        protected s_FeetLedge(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Up on the ledge: walking on at the running speed (unarmed) or armed.</summary>
        [Uc("SBase.SPlayerController.s_FeetLedge.AnimEnd", Sha1 = "1fdc246f6dc5d73f5c133daa4380bd259694aaba")]
        [PortOf("uc:SBase.SPlayerController:s_FeetLedge.AnimEnd", Spec = "spec/movement/barrier-and-low-ledges.md#climb-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            pc.MovementCameraSmooth(CameraSmoothDefault);
            if (pawn.WeaponLoaded == 0)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H_AFTERFL);
                pc.eventChangeState(MovementStateNames.s_PlayerWalkingSt0H);
            }
            else
            {
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_2H_AFTERFL);
                pc.eventChangeState(MovementStateNames.s_PlayerWalkingSt2H);
            }
        }

        [Uc("SBase.SPlayerController.s_FeetLedge.PlayerMove", Sha1 = "72813a886391dd36f71f3c47d4ae1e22778ea870")]
        [PortOf("uc:SBase.SPlayerController:s_FeetLedge.PlayerMove", Spec = "spec/movement/barrier-and-low-ledges.md#climb-states")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn?.SPawnType == PawnType.SPAWNOFFENSE)
            {
                self.UpdateCamRotation(DeltaTime);
            }

            self.bPressedJump = false;
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_WaistLedgeDetect")]
    public class s_WaistLedgeDetect : UcActorState, IPlayerMove, ITopSynchro, INoGE, ICheckFeetZone
    {
        public static readonly s_WaistLedgeDetect Instance = new(UcMeta.s_WaistLedgeDetect, null);

        protected s_WaistLedgeDetect(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_WaistLedgeDetect.CheckFeetZone", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_WaistLedgeDetect.CheckFeetZone", Spec = "spec/movement/barrier-and-low-ledges.md#detect-states-repositioning")]
        public virtual void CheckFeetZone(SPlayerController self)
        {
        }

        [Uc("SBase.SPlayerController.s_WaistLedgeDetect.BeginState", Sha1 = "0273d81b27e7ee1a21810bb6158313a4e34a46e0")]
        [PortOf("uc:SBase.SPlayerController:s_WaistLedgeDetect.BeginState", Spec = "spec/movement/barrier-and-low-ledges.md#detect-states-repositioning")]
        public override void BeginState(Actor self) => ((SPlayerController)self).bCanStop = false;

        /// <summary>The grab clip ended: remember completion until the hold reports its settled position.</summary>
        [Uc("SBase.SPlayerController.s_WaistLedgeDetect.AnimEnd", Sha1 = "3b8904aae8969f9f7b1c7702aa1f82096be42985")]
        [PortOf("uc:SBase.SPlayerController:s_WaistLedgeDetect.AnimEnd", Spec = "spec/movement/barrier-and-low-ledges.md#detect-states-repositioning")]
        public override void AnimEnd(Actor self, int Channel)
        {
            if (Channel == 0 && ((SPlayerController)self).IAmOnClient())
            {
                ((SPlayerController)self).bCanStop = true;
            }
        }

        /// <summary>The snap is done: climb once the grab animation has also completed.</summary>
        [Uc("SBase.SPlayerController.s_WaistLedgeDetect.TopSynchro", Sha1 = "fcd5734d8841fc68fc373a0601404c7eb9bd763d")]
        [PortOf("uc:SBase.SPlayerController:s_WaistLedgeDetect.TopSynchro", Spec = "spec/movement/barrier-and-low-ledges.md#detect-states-repositioning")]
        public virtual void TopSynchro(SPlayerController self) => self.WaistLedgeStep();

        [Uc("SBase.SPlayerController.s_WaistLedgeDetect.NoGE", Sha1 = "19424536ab48917d333749da8b5b85d5f718897f")]
        [PortOf("uc:SBase.SPlayerController:s_WaistLedgeDetect.NoGE", Spec = "spec/movement/barrier-and-low-ledges.md#detect-states-repositioning")]
        public virtual void NoGE(SPlayerController self) => self.LowLedgeLost();

        [Uc("SBase.SPlayerController.s_WaistLedgeDetect.PlayerMove", Sha1 = "fa0422aa7a6ba9b3653e859ef7901407a3a0009e")]
        [PortOf("uc:SBase.SPlayerController:s_WaistLedgeDetect.PlayerMove", Spec = "spec/movement/barrier-and-low-ledges.md#detect-states-repositioning")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime) => self.CamViewNoMove(DeltaTime);
    }

    [Uc("SBase.SPlayerController.s_WaistLedge")]
    public class s_WaistLedge : UcActorState, IPlayerMove
    {
        public static readonly s_WaistLedge Instance = new(UcMeta.s_WaistLedge, null);

        protected s_WaistLedge(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_WaistLedge.AnimEnd", Sha1 = "c7b2aee6bb546aab34b553aa1608b2f4e4355222")]
        [PortOf("uc:SBase.SPlayerController:s_WaistLedge.AnimEnd", Spec = "spec/movement/barrier-and-low-ledges.md#climb-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pc.MovementCameraSmooth(CameraSmoothDefault);
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_ST_0H);
                pc.eventChangeState(MovementStateNames.s_PlayerWalkingSt0H);
            }
        }

        [Uc("SBase.SPlayerController.s_WaistLedge.PlayerMove", Sha1 = "45eed3576fd0e0a37a85fee9e8e4afb15f742255")]
        [PortOf("uc:SBase.SPlayerController:s_WaistLedge.PlayerMove", Spec = "spec/movement/barrier-and-low-ledges.md#climb-states")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateCamRotation(DeltaTime);
            self.bPressedJump = false;
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_WaistLedge_Cr")]
    public class s_WaistLedge_Cr : s_WaistLedge
    {
        public static new readonly s_WaistLedge_Cr Instance = new(UcMeta.s_WaistLedge_Cr, s_WaistLedge.Instance);

        protected s_WaistLedge_Cr(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_WaistLedge_Cr.BeginState", Sha1 = "0741485055f029318430c3b61e9fd0b580cbef59")]
        [PortOf("uc:SBase.SPlayerController:s_WaistLedge_Cr.BeginState", Spec = "spec/movement/barrier-and-low-ledges.md#climb-states")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.MovementCamera(ECamMode.ECM_SmoothTransitionCentered);
            pc.SBPawn?.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
        }

        /// <summary>Up on the ledge crouched: crouched walking with the crouch latch on.</summary>
        [Uc("SBase.SPlayerController.s_WaistLedge_Cr.AnimEnd", Sha1 = "f610566a5b99b8536388edda12d5a0a149712794")]
        [PortOf("uc:SBase.SPlayerController:s_WaistLedge_Cr.AnimEnd", Spec = "spec/movement/barrier-and-low-ledges.md#climb-states")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pc.MovementCameraSmooth(CameraSmoothDefault);
                pc.bCrouchWithRoll = true;
                pawn.ChangeEnumAction(SBasePawn.SA_WALK_CR_0H);
                pawn.ChangeEnumAction(SBasePawn.SA_WANTS_TO_CROUCH);
                pc.eventChangeState(MovementStateNames.s_PlayerWalkingCr0H);
            }
        }
    }
}
