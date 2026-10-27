using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController hand-over-hand states (HOH GE): s_PipeH_Base and the grab, hands, legs-over, turn and
// perpendicular-bar states, the changes to a vertical pipe, and the fire poses. Written from
// spec/movement/pipe-horizontal.md.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_PipeH_Base")]
    public class s_PipeH_Base : UcActorState, ICanActivateLaser, ISwitchBinocularVision
    {
        public static readonly s_PipeH_Base Instance = new(UcMeta.s_PipeH_Base, null);

        protected s_PipeH_Base(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>
        /// The binocular key does nothing on the bar, in every child state: without this the global version would
        /// holster a drawn pistol (bForceExitFireMode) in the hanging fire states.
        /// </summary>
        [Uc("SBase.SPlayerController.s_PipeH_Base.SwitchBinocularVision", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_PipeH_Base.SwitchBinocularVision", Spec = "spec/movement/pipe-horizontal.md#s_pipeh_base")]
        public virtual void SwitchBinocularVision(SPlayerController self)
        {
        }

        [Uc("SBase.SPlayerController.s_PipeH_Base.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_PipeH_Base.CanActivateLaser", Spec = "spec/movement/pipe-horizontal.md#s_pipeh_base")]
        public virtual bool CanActivateLaser(SPlayerController self) => false;

        /// <summary>The drop from the bar with the crouch latch: the zip drop (MyDoJump with the bar's velocity), the jump state.</summary>
        internal static void DropFromBar(SPlayerController self)
        {
            self.bCrouchWithRoll = false;
            self.bPressedJump = false;
            if (self.SBPawn is { } pawn)
            {
                pawn.MyDoJump();
                pawn.ChangeEnumAction(SBasePawn.SA_ZIPFALL);
            }

            self.eventChangeState(MovementStateNames.s_PlayerJump);
        }

        /// <summary>A vertical pipe at the end of the bar: down or up onto it.</summary>
        internal static void BarToPipe(SPlayerController self)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.SendZipPAndNormalToServer();
            float zMin = pawn.ZipP1.Z > pawn.ZipP2.Z ? pawn.ZipP2.Z : pawn.ZipP1.Z;
            self.bFindGE = true;
            self.MovementCameraSmooth(MovementK.HohSmooth);
            pawn.ChangeEnumAction(zMin < pawn.Location.Z ? SBasePawn.SA_TR_PIPEH_V : SBasePawn.SA_TR_PIPEH_V_UP);
            self.eventChangeState(MovementStateNames.s_Tr_PipeH2V);
        }

        /// <summary>A zip line at the end of the bar: the zip grab.</summary>
        internal static void BarToZip(SPlayerController self)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.SendZipPToServer();
            pawn.ChangeEnumAction(SBasePawn.SA_ZIP_GRAB);
            self.eventChangeState(MovementStateNames.s_GrabZip);
        }
    }

    [Uc("SBase.SPlayerController.s_GrabPipH")]
    public class s_GrabPipH : s_PipeH_Base, IPlayerMove, IPipe, IZipLine, INoGE
    {
        public static new readonly s_GrabPipH Instance = new(UcMeta.s_GrabPipH, s_PipeH_Base.Instance);

        protected s_GrabPipH(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_GrabPipH.AnimEnd", Sha1 = "9b3f4ab5f5af83eb8963328247cb657041e813f4")]
        [PortOf("uc:SBase.SPlayerController:s_GrabPipH.AnimEnd", Spec = "spec/movement/pipe-horizontal.md#s_grabpiph")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_PIPEH);
                pc.eventChangeState(MovementStateNames.s_OnPipeH);
            }
        }

        [Uc("SBase.SPlayerController.s_GrabPipH.Pipe", Sha1 = "cf1c33c2633f352c3a4228460ea82753754a0412")]
        [PortOf("uc:SBase.SPlayerController:s_GrabPipH.Pipe", Spec = "spec/movement/pipe-horizontal.md#s_grabpiph")]
        public virtual void Pipe(SPlayerController self, FVector locationEnd, FRotator orientationEnd) => BarToPipe(self);

        [Uc("SBase.SPlayerController.s_GrabPipH.ZipLine", Sha1 = "ac7b2fcf61f27b49e8ef83c37cc8d357b7f0b578")]
        [PortOf("uc:SBase.SPlayerController:s_GrabPipH.ZipLine", Spec = "spec/movement/pipe-horizontal.md#s_grabpiph")]
        public virtual void ZipLine(SPlayerController self, FVector locationEnd, FRotator orientationEnd) => BarToZip(self);

        [Uc("SBase.SPlayerController.s_GrabPipH.EndState", Sha1 = "2a0b741602c58395b1c79ef18b417548327b41cf")]
        [PortOf("uc:SBase.SPlayerController:s_GrabPipH.EndState", Spec = "spec/movement/pipe-horizontal.md#s_grabpiph")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
        }

        /// <summary>The bar was lost while grabbing it: fall, and the same bar is not caught again.</summary>
        [Uc("SBase.SPlayerController.s_GrabPipH.NoGE", Sha1 = "19424536ab48917d333749da8b5b85d5f718897f")]
        [PortOf("uc:SBase.SPlayerController:s_GrabPipH.NoGE", Spec = "spec/movement/pipe-horizontal.md#s_grabpiph")]
        public virtual void NoGE(SPlayerController self)
        {
            if (!self.IAmOnClient() || self.SBPawn is not { } pawn)
            {
                return;
            }

            pawn.ChangeEnumAction(SBasePawn.SA_LEDGEFALL);
            pawn.LastZipP = pawn.ZipP1;
            self.eventChangeState(MovementStateNames.s_PlayerJump);
        }

        [Uc("SBase.SPlayerController.s_GrabPipH.PlayerMove", Sha1 = "d16b1800f8c6773f476a21f6816e71901b76ca28")]
        [PortOf("uc:SBase.SPlayerController:s_GrabPipH.PlayerMove", Spec = "spec/movement/pipe-horizontal.md#s_grabpiph")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateCamRotation(DeltaTime);
            if (self.bCrouchWithRoll)
            {
                DropFromBar(self);
            }

            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_OnPipeH")]
    public class s_OnPipeH : s_PipeH_Base, IPlayerMove, IZipLine, IPipe, IOrthoPipeH, ICanUseCam
    {
        public static new readonly s_OnPipeH Instance = new(UcMeta.s_OnPipeH, s_PipeH_Base.Instance);

        protected s_OnPipeH(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_OnPipeH.CanUseCam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipeH.CanUseCam", Spec = "spec/movement/pipe-horizontal.md#moving-by-the-hands-s_onpipeh")]
        public virtual bool CanUseCam(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_OnPipeH.BeginState", Sha1 = "f8b1a02ddfb9bd3e57c5d9d0a2643db9170009d3")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipeH.BeginState", Spec = "spec/movement/pipe-horizontal.md#moving-by-the-hands-s_onpipeh")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.MovementCamera(ECamMode.ECM_NoTargetCol);
            pc.bPressedJump = false;
            pc.bCrouchWithRoll = false;
        }

        [Uc("SBase.SPlayerController.s_OnPipeH.ZipLine", Sha1 = "ac7b2fcf61f27b49e8ef83c37cc8d357b7f0b578")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipeH.ZipLine", Spec = "spec/movement/pipe-horizontal.md#moving-by-the-hands-s_onpipeh")]
        public virtual void ZipLine(SPlayerController self, FVector locationEnd, FRotator orientationEnd) => BarToZip(self);

        /// <summary>
        /// Hanging by the hands: the crouch button held drops, jump swings the legs over when there is room, the fire
        /// mode draws the pistol, Forward or Up moves along the bar at 126, Back turns round.
        /// </summary>
        [Uc("SBase.SPlayerController.s_OnPipeH.PlayerMove", Sha1 = "3b000684c63e73e9e113289e0071d2d0a9ac316f")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipeH.PlayerMove", Spec = "spec/movement/pipe-horizontal.md#moving-by-the-hands-s_onpipeh")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            FVector newAccel = default;
            if (self.bCrouch > 0)
            {
                self.bPressedJump = false;
                pawn.MyDoJump();
                pawn.ChangeEnumAction(SBasePawn.SA_ZIPFALL);
                self.bCrouchWithRoll = false;
                self.eventChangeState(MovementStateNames.s_PlayerJump);
            }
            else
            {
                if (self.bPressedJump)
                {
                    if (self.bCanGoInPipe4())
                    {
                        pawn.ChangeEnumAction(SBasePawn.SA_PIPH_UP4);
                        self.eventChangeState(MovementStateNames.s_PipH_TR2_4);
                    }
                    else
                    {
                        self.bPressedJump = false;
                    }
                }

                if (!self.bPressedJump)
                {
                    if (self.bForceFireMode && !pawn.bStickyShocking)
                    {
                        self.bForceFireMode = false;
                        if (self.bForceLaserFireMode)
                        {
                            MovementService.HooksOf(self).SwitchLaser(self, true);
                        }

                        pawn.ChangeEnumAction(SBasePawn.SA_PIPH_TR_FIRE2);
                        self.eventChangeState(MovementStateNames.s_PipH_TR2Fire2);
                    }
                    else if ((self.iMove & (cMoveForward | cMoveUp)) != 0)
                    {
                        newAccel.X = MovementK.HohMoveRequest;
                    }
                    else if ((self.iMove & cMoveBack) != 0)
                    {
                        pawn.ChangeEnumAction(SBasePawn.SA_PIPH_TURN);
                        self.eventChangeState(MovementStateNames.s_PipH_Return);
                    }
                }
            }

            self.UpdateCamRotation(DeltaTime);
            self.SendMove(DeltaTime, newAccel);
        }

        [Uc("SBase.SPlayerController.s_OnPipeH.Pipe", Sha1 = "7037232bf9aad7cd0ab37b0a9feb06e7d3a59f8f")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipeH.Pipe", Spec = "spec/movement/pipe-horizontal.md#moving-by-the-hands-s_onpipeh")]
        public virtual void Pipe(SPlayerController self, FVector locationEnd, FRotator orientationEnd) => BarToPipe(self);

        /// <summary>A perpendicular bar: the turn onto it (the controlling side takes its points first).</summary>
        [Uc("SBase.SPlayerController.s_OnPipeH.OrthoPipeH", Sha1 = "f43f66a75b51cbab46e6e08d7c28b3204fe48052")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipeH.OrthoPipeH", Spec = "spec/movement/pipe-horizontal.md#moving-by-the-hands-s_onpipeh")]
        public virtual void OrthoPipeH(SPlayerController self, bool bToLeft)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            if (self.IAmOnClient())
            {
                pawn.ZipP1 = pawn.NewZipP1;
                pawn.ZipP2 = pawn.NewZipP2;
                pawn.SendZipPToServer();
            }

            pawn.ChangeEnumAction(bToLeft ? SBasePawn.SA_TR_ORTHOPIPE_L : SBasePawn.SA_TR_ORTHOPIPE_R);
            self.eventChangeState(MovementStateNames.s_OrthoPipe_Tr);
        }
    }

    [Uc("SBase.SPlayerController.s_OrthoPipe_Tr")]
    public class s_OrthoPipe_Tr : s_PipeH_Base, IPlayerMove
    {
        public static new readonly s_OrthoPipe_Tr Instance = new(UcMeta.s_OrthoPipe_Tr, s_PipeH_Base.Instance);

        protected s_OrthoPipe_Tr(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_OrthoPipe_Tr.AnimEnd", Sha1 = "02e5508b42d117ce9959f17ab4f1e958589cf48d")]
        [PortOf("uc:SBase.SPlayerController:s_OrthoPipe_Tr.AnimEnd", Spec = "spec/movement/pipe-horizontal.md#moving-by-the-hands-s_onpipeh")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pc.MovementCameraSmooth(MovementK.HohSmooth);
                pawn.ChangeEnumAction(SBasePawn.SA_ON_PIPEH);
                pc.eventChangeState(MovementStateNames.s_OnPipeH);
            }
        }

        [Uc("SBase.SPlayerController.s_OrthoPipe_Tr.PlayerMove", Sha1 = "45d44fb1b81421037cc146be861746e0f8998f9c")]
        [PortOf("uc:SBase.SPlayerController:s_OrthoPipe_Tr.PlayerMove", Spec = "spec/movement/pipe-horizontal.md#moving-by-the-hands-s_onpipeh")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateCamRotation(DeltaTime);
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_Tr_PipeH2V")]
    public class s_Tr_PipeH2V : s_GrabPipH
    {
        public static new readonly s_Tr_PipeH2V Instance = new(UcMeta.s_Tr_PipeH2V, s_GrabPipH.Instance);

        protected s_Tr_PipeH2V(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>Onto the vertical pipe: climbing it.</summary>
        [Uc("SBase.SPlayerController.s_Tr_PipeH2V.AnimEnd", Sha1 = "927b4d36857fe736f0cf0d55a9d4fb0c41f1621d")]
        [PortOf("uc:SBase.SPlayerController:s_Tr_PipeH2V.AnimEnd", Spec = "spec/movement/pipe-horizontal.md#moving-by-the-hands-s_onpipeh")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pc.MovementCameraSmooth(MovementK.HohSmooth);
                pawn.ChangeEnumAction(SBasePawn.SA_ON_PIPE);
                pc.eventChangeState(MovementStateNames.s_OnPipe);
            }
        }
    }

    [Uc("SBase.SPlayerController.s_PipH_Return")]
    public class s_PipH_Return : s_PipeH_Base, IPlayerMove
    {
        public static new readonly s_PipH_Return Instance = new(UcMeta.s_PipH_Return, s_PipeH_Base.Instance);

        protected s_PipH_Return(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The pawn faces the other way along the bar.</summary>
        [Uc("SBase.SPlayerController.s_PipH_Return.BeginState", Sha1 = "08bea4f979d6e34e870b92fd12b009516f0bd3c5")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Return.BeginState", Spec = "spec/movement/pipe-horizontal.md#turning-round-s_piph_return")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.SBPawn is { } pawn && pc.Pawn is { } p)
            {
                pawn.Rotation = (-p.Rotation.Vector()).Rotation();
            }
        }

        [Uc("SBase.SPlayerController.s_PipH_Return.AnimEnd", Sha1 = "da6a7be95ad05e97a6cfbc98d0bf74aeaac23b03")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Return.AnimEnd", Spec = "spec/movement/pipe-horizontal.md#turning-round-s_piph_return")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_PIPEH);
                pc.eventChangeState(MovementStateNames.s_OnPipeH);
            }
        }

        [Uc("SBase.SPlayerController.s_PipH_Return.PlayerMove", Sha1 = "bdece539398bb2af98dd47aa8d2114973f369249")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Return.PlayerMove", Spec = "spec/movement/pipe-horizontal.md#turning-round-s_piph_return")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.bCrouchWithRoll)
            {
                DropFromBar(self);
            }

            self.UpdateCamRotation(DeltaTime);
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_PipH_TR2_4")]
    public class s_PipH_TR2_4 : s_PipeH_Base, IPlayerMove
    {
        public static new readonly s_PipH_TR2_4 Instance = new(UcMeta.s_PipH_TR2_4, s_PipeH_Base.Instance);

        protected s_PipH_TR2_4(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PipH_TR2_4.AnimEnd", Sha1 = "627be50ec277422a1c31bec68d66feec211e6f19")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_TR2_4.AnimEnd", Spec = "spec/movement/pipe-horizontal.md#stance-4-legs-over-the-pipe")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_PIPEH_4);
                pc.eventChangeState(MovementStateNames.s_OnPipeH_4);
            }
        }

        [Uc("SBase.SPlayerController.s_PipH_TR2_4.EndState", Sha1 = "2a0b741602c58395b1c79ef18b417548327b41cf")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_TR2_4.EndState", Spec = "spec/movement/pipe-horizontal.md#stance-4-legs-over-the-pipe")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
        }

        [Uc("SBase.SPlayerController.s_PipH_TR2_4.PlayerMove", Sha1 = "8deb7cc8097cc394f9f3f703f06a87053822ce81")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_TR2_4.PlayerMove", Spec = "spec/movement/pipe-horizontal.md#stance-4-legs-over-the-pipe")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateCamRotation(DeltaTime);
            self.bPressedJump = false;
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_OnPipeH_4")]
    public class s_OnPipeH_4 : s_PipeH_Base, IPlayerMove, ICanUseCam
    {
        public static new readonly s_OnPipeH_4 Instance = new(UcMeta.s_OnPipeH_4, s_PipeH_Base.Instance);

        protected s_OnPipeH_4(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_OnPipeH_4.CanUseCam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipeH_4.CanUseCam", Spec = "spec/movement/pipe-horizontal.md#stance-4-legs-over-the-pipe")]
        public virtual bool CanUseCam(SPlayerController self) => true;

        /// <summary>
        /// Legs over the bar: jump swings back to the hands, crouch drops, the fire mode draws (with room), Forward and
        /// Back crawl at 113.
        /// </summary>
        [Uc("SBase.SPlayerController.s_OnPipeH_4.PlayerMove", Sha1 = "8a2812bd7efecfa67b7b87dfbfe5378959deca2b")]
        [PortOf("uc:SBase.SPlayerController:s_OnPipeH_4.PlayerMove", Spec = "spec/movement/pipe-horizontal.md#stance-4-legs-over-the-pipe")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            FVector newAccel = default;
            if (self.bPressedJump)
            {
                self.bPressedJump = false;
                if (self.TestRoomAfterPipe4())
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_PIPH_DOWN2);
                    self.eventChangeState(MovementStateNames.s_PipH_TR2_2);
                }
            }
            else
            {
                if (self.bCrouchWithRoll)
                {
                    if (self.TestRoomAfterPipe4())
                    {
                        pawn.ChangeEnumAction(SBasePawn.SA_PIPH_JUMP4);
                        self.eventChangeState(MovementStateNames.s_PlayerJump);
                        self.bCrouchWithRoll = false;
                    }
                }
                else if (self.bForceFireMode && !pawn.bStickyShocking)
                {
                    if (self.bForceLaserFireMode)
                    {
                        MovementService.HooksOf(self).SwitchLaser(self, true);
                    }

                    self.bForceFireMode = false;
                    if (self.TestRoomAfterPipe4())
                    {
                        pawn.ChangeEnumAction(SBasePawn.SA_PIPH_TR_FIRE4);
                        self.eventChangeState(MovementStateNames.s_PipH_TR2Fire4);
                    }
                    else
                    {
                        self.bFireMode = 0;
                    }
                }

                if ((self.iMove & cMoveForward) != 0)
                {
                    newAccel.X = MovementK.HohLegsOverMoveRequest;
                }
                else if ((self.iMove & cMoveBack) != 0)
                {
                    newAccel.X = -MovementK.HohLegsOverMoveRequest;
                }
            }

            self.UpdateCamRotation(DeltaTime);
            self.SendMove(DeltaTime, newAccel);
        }
    }

    [Uc("SBase.SPlayerController.s_PipH_TR2_2")]
    public class s_PipH_TR2_2 : s_PipeH_Base, IPlayerMove
    {
        public static new readonly s_PipH_TR2_2 Instance = new(UcMeta.s_PipH_TR2_2, s_PipeH_Base.Instance);

        protected s_PipH_TR2_2(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PipH_TR2_2.AnimEnd", Sha1 = "9656162f50ecd1e66459b884a00147f1acb1a5eb")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_TR2_2.AnimEnd", Spec = "spec/movement/pipe-horizontal.md#stance-4-legs-over-the-pipe")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_PIPEH);
                pc.eventChangeState(MovementStateNames.s_OnPipeH);
            }
        }

        [Uc("SBase.SPlayerController.s_PipH_TR2_2.EndState", Sha1 = "2a0b741602c58395b1c79ef18b417548327b41cf")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_TR2_2.EndState", Spec = "spec/movement/pipe-horizontal.md#stance-4-legs-over-the-pipe")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
        }

        [Uc("SBase.SPlayerController.s_PipH_TR2_2.PlayerMove", Sha1 = "9a34dd321b25cc95792c7896286225a7895a0cd6")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_TR2_2.PlayerMove", Spec = "spec/movement/pipe-horizontal.md#stance-4-legs-over-the-pipe")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateCamRotation(DeltaTime);
            self.bPressedJump = false;
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_PipH_TR2Fire2")]
    public class s_PipH_TR2Fire2 : s_PipeH_Base, IPlayerMove, IAltFire
    {
        public static new readonly s_PipH_TR2Fire2 Instance = new(UcMeta.s_PipH_TR2Fire2, null);

        protected s_PipH_TR2Fire2(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PipH_TR2Fire2.AltFire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_TR2Fire2.AltFire", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public virtual void AltFire(SPlayerController self, float F)
        {
        }

        [Uc("SBase.SPlayerController.s_PipH_TR2Fire2.AnimEnd", Sha1 = "2a165e7bacbbec22e8cf504c92a255d6345bcd0d")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_TR2Fire2.AnimEnd", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_PIPH_FIRE2);
                pc.eventChangeState(MovementStateNames.s_PipH_Fire2);
            }
        }

        [Uc("SBase.SPlayerController.s_PipH_TR2Fire2.EndState", Sha1 = "df26c6762a09ce60a7e3b5fd97c901fef379aaf8")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_TR2Fire2.EndState", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
            pc.MovementCamera(ECamMode.ECM_HOHFP);
        }

        [Uc("SBase.SPlayerController.s_PipH_TR2Fire2.PlayerMove", Sha1 = "b7d18e073afa90bb7e2a57f6d4ca30b5c22db2a4")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_TR2Fire2.PlayerMove", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.Pawn is { } p)
            {
                self.UpdateLimitedRotation(DeltaTime, -MovementK.HohFire2Yaw, MovementK.HohFire2Yaw, MovementK.HohFire2PitchMin,
                    MovementK.HohFire2PitchMax, p.Rotation.Yaw, 0);
            }

            if (self.bCrouchWithRoll)
            {
                DropFromBar(self);
            }

            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_PipH_Fire2")]
    public class s_PipH_Fire2 : s_PipeH_Base, IPlayerMove, ICanUseCam, ISwitchLaser
    {
        public static new readonly s_PipH_Fire2 Instance = new(UcMeta.s_PipH_Fire2, s_PipeH_Base.Instance);

        protected s_PipH_Fire2(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PipH_Fire2.CanActivateLaser", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire2.CanActivateLaser", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public override bool CanActivateLaser(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PipH_Fire2.CanUseCam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire2.CanUseCam", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public virtual bool CanUseCam(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PipH_Fire2.SwitchLaser", Sha1 = "c7a4b9666591efeb2990524b26078f1157137c90")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire2.SwitchLaser", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public virtual void SwitchLaser(SPlayerController self) => self.WeaponSwitchLaser();

        /// <summary>The pistol out under the bar: holster on release (or a jump press); crouch drops.</summary>
        [Uc("SBase.SPlayerController.s_PipH_Fire2.PlayerMove", Sha1 = "27b402308a84c7bd8076ea5ba7f94011886e4415")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire2.PlayerMove", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            if ((self.bForceExitFireMode || (self.bTestBoutonLache && self.bFireMode == 1) || self.bPressedJump) && !pawn.bStickyShocking)
            {
                HolsterOnBar(self, ECamMode.ECM_NoTargetCol);
                pawn.ChangeEnumAction(SBasePawn.SA_PIPH_FIRE_TR2);
                self.eventChangeState(MovementStateNames.s_PipH_Fire2TR2);
            }

            self.LimitedViewByCamera(DeltaTime, pawn.Rotation.Yaw, 0);
            if (self.bCrouchWithRoll)
            {
                DropFromBar(self);
            }

            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_PipH_Fire2TR2")]
    public class s_PipH_Fire2TR2 : s_PipeH_Base, IPlayerMove, IAltFire
    {
        public static new readonly s_PipH_Fire2TR2 Instance = new(UcMeta.s_PipH_Fire2TR2, null);

        protected s_PipH_Fire2TR2(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PipH_Fire2TR2.AltFire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire2TR2.AltFire", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public virtual void AltFire(SPlayerController self, float F)
        {
        }

        [Uc("SBase.SPlayerController.s_PipH_Fire2TR2.AnimEnd", Sha1 = "b37e0d4a383de880906ad15c6bfa81d12941d2c2")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire2TR2.AnimEnd", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_PIPEH);
                pc.eventChangeState(MovementStateNames.s_OnPipeH);
            }
        }

        [Uc("SBase.SPlayerController.s_PipH_Fire2TR2.EndState", Sha1 = "2a0b741602c58395b1c79ef18b417548327b41cf")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire2TR2.EndState", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
        }

        [Uc("SBase.SPlayerController.s_PipH_Fire2TR2.PlayerMove", Sha1 = "2161e62ab55045884244cc2bba740c923c6f7646")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire2TR2.PlayerMove", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.bCrouchWithRoll)
            {
                DropFromBar(self);
            }

            self.UpdateCamRotation(DeltaTime);
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_PipH_TR2Fire4")]
    public class s_PipH_TR2Fire4 : s_PipeH_Base, IPlayerMove, IAltFire
    {
        public static new readonly s_PipH_TR2Fire4 Instance = new(UcMeta.s_PipH_TR2Fire4, null);

        protected s_PipH_TR2Fire4(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PipH_TR2Fire4.AltFire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_TR2Fire4.AltFire", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public virtual void AltFire(SPlayerController self, float F)
        {
        }

        [Uc("SBase.SPlayerController.s_PipH_TR2Fire4.AnimEnd", Sha1 = "507fe9bc8788f69c6be5a082070270e0b3a49061")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_TR2Fire4.AnimEnd", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_PIPH_FIRE4);
                pc.eventChangeState(MovementStateNames.s_PipH_Fire4);
            }
        }

        [Uc("SBase.SPlayerController.s_PipH_TR2Fire4.EndState", Sha1 = "4a19ec035bf0d487b266a6607a030a9879b7a794")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_TR2Fire4.EndState", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
            pc.MovementCamera(ECamMode.ECM_HOHFPHanged);
        }

        [Uc("SBase.SPlayerController.s_PipH_TR2Fire4.PlayerMove", Sha1 = "35716dab1fd4d428c634b629835420bc5010e6a8")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_TR2Fire4.PlayerMove", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.Pawn is { } p)
            {
                self.UpdateLimitedRotation(DeltaTime, -MovementK.HohFire4TransitionYaw, MovementK.HohFire4TransitionYaw,
                    -MovementK.HohFire4Pitch, MovementK.HohFire4Pitch, 32768 + p.Rotation.Yaw, 0);
            }

            self.bPressedJump = false;
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_PipH_Fire4")]
    public class s_PipH_Fire4 : s_PipeH_Base, IPlayerMove, ICanUseCam, ISwitchLaser
    {
        public static new readonly s_PipH_Fire4 Instance = new(UcMeta.s_PipH_Fire4, s_PipeH_Base.Instance);

        protected s_PipH_Fire4(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PipH_Fire4.CanActivateLaser", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire4.CanActivateLaser", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public override bool CanActivateLaser(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PipH_Fire4.CanUseCam", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire4.CanUseCam", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public virtual bool CanUseCam(SPlayerController self) => true;

        [Uc("SBase.SPlayerController.s_PipH_Fire4.SwitchLaser", Sha1 = "c7a4b9666591efeb2990524b26078f1157137c90")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire4.SwitchLaser", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public virtual void SwitchLaser(SPlayerController self) => self.WeaponSwitchLaser();

        /// <summary>The pistol out upside down, aiming behind: holster on release; crouch drops with the pistol out.</summary>
        [Uc("SBase.SPlayerController.s_PipH_Fire4.PlayerMove", Sha1 = "7b6086c903ae0a43244df901c81c0ea459690f60")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire4.PlayerMove", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            if ((self.bForceExitFireMode || (self.bTestBoutonLache && self.bFireMode == 1) || self.bPressedJump) && !pawn.bStickyShocking)
            {
                HolsterOnBar(self, ECamMode.ECM_Walking);
                pawn.ChangeEnumAction(SBasePawn.SA_PIPH_FIRE_TR4);
                self.eventChangeState(MovementStateNames.s_PipH_Fire2TR4);
            }

            if (self.bCrouchWithRoll)
            {
                self.bFireMode = 0;
                self.bForceFireMode = false;
                self.bTestBoutonLache = false;
                self.MovementCamera(ECamMode.ECM_Walking);
                pawn.ChangeEnumAction(SBasePawn.SA_PIPH_JUMPFIRE4);
                self.eventChangeState(MovementStateNames.s_PlayerJump);
                self.bCrouchWithRoll = false;
            }

            self.UpdateLimitedRotation(DeltaTime, -MovementK.HohFire4Yaw, MovementK.HohFire4Yaw, -MovementK.HohFire4Pitch,
                MovementK.HohFire4Pitch, 32768 + pawn.Rotation.Yaw, 0);
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_PipH_Fire2TR4")]
    public class s_PipH_Fire2TR4 : s_PipeH_Base, IPlayerMove, IAltFire
    {
        public static new readonly s_PipH_Fire2TR4 Instance = new(UcMeta.s_PipH_Fire2TR4, null);

        protected s_PipH_Fire2TR4(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_PipH_Fire2TR4.AltFire", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire2TR4.AltFire", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public virtual void AltFire(SPlayerController self, float F)
        {
        }

        [Uc("SBase.SPlayerController.s_PipH_Fire2TR4.AnimEnd", Sha1 = "4bef93a9cbb0a4479fbe0655348bccb5a5e12cc9")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire2TR4.AnimEnd", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_PIPEH_4);
                pc.eventChangeState(MovementStateNames.s_OnPipeH_4);
            }
        }

        [Uc("SBase.SPlayerController.s_PipH_Fire2TR4.EndState", Sha1 = "2a0b741602c58395b1c79ef18b417548327b41cf")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire2TR4.EndState", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public override void EndState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.m_LastSpecialMoveTime = pc.Level.TimeSeconds;
        }

        [Uc("SBase.SPlayerController.s_PipH_Fire2TR4.PlayerMove", Sha1 = "035a8c08bb3cf835c696cc93091d344bb473c519")]
        [PortOf("uc:SBase.SPlayerController:s_PipH_Fire2TR4.PlayerMove", Spec = "spec/movement/pipe-horizontal.md#firing-from-the-pipe")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.UpdateCamRotation(DeltaTime);
            self.bPressedJump = false;
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_Tr_PipeV2H")]
    public class s_Tr_PipeV2H : s_GrabPipe
    {
        public static new readonly s_Tr_PipeV2H Instance = new(UcMeta.s_Tr_PipeV2H, s_GrabPipe.Instance);

        protected s_Tr_PipeV2H(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The turn from the vertical pipe onto the bar ended: hanging by the hands.</summary>
        [Uc("SBase.SPlayerController.s_Tr_PipeV2H.AnimEnd", Sha1 = "005262e12f5e6d5fcaad60dca926a664eb505448")]
        [PortOf("uc:SBase.SPlayerController:s_Tr_PipeV2H.AnimEnd", Spec = "spec/movement/pipe-vertical.md#to-hand-over-hand-s_tr_pipev2h_oriente--_orientedown-s_tr_pipev2h")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pc.MovementCameraSmooth(MovementK.HohSmooth);
                pawn.ChangeEnumAction(SBasePawn.SA_ON_PIPEH);
                pc.eventChangeState(MovementStateNames.s_OnPipeH);
            }
        }
    }

    [Uc("SBase.SPlayerController.s_Tr_PipeV2H_Oriente")]
    public class s_Tr_PipeV2H_Oriente : s_GrabPipe, ITopSynchro
    {
        public static new readonly s_Tr_PipeV2H_Oriente Instance = new(UcMeta.s_Tr_PipeV2H_Oriente, s_GrabPipe.Instance);

        protected s_Tr_PipeV2H_Oriente(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The grab clip's end is ignored while the pipe hold turns the pawn toward the bar.</summary>
        public override void AnimEnd(Actor self, int Channel)
        {
        }

        /// <summary>Facing the bar above: the turn onto it.</summary>
        [Uc("SBase.SPlayerController.s_Tr_PipeV2H_Oriente.TopSynchro", Sha1 = "5c2849aaf10ad06f8cfb21c87fc53f8a5d18caec")]
        [PortOf("uc:SBase.SPlayerController:s_Tr_PipeV2H_Oriente.TopSynchro", Spec = "spec/movement/pipe-vertical.md#to-hand-over-hand-s_tr_pipev2h_oriente--_orientedown-s_tr_pipev2h")]
        public virtual void TopSynchro(SPlayerController self)
        {
            if (self.IAmOnClient() && self.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_TR_PIPEV_H);
                self.eventChangeState(MovementStateNames.s_Tr_PipeV2H);
            }
        }
    }

    [Uc("SBase.SPlayerController.s_Tr_PipeV2H_OrienteDown")]
    public class s_Tr_PipeV2H_OrienteDown : s_GrabPipe, ITopSynchro
    {
        public static new readonly s_Tr_PipeV2H_OrienteDown Instance = new(UcMeta.s_Tr_PipeV2H_OrienteDown, s_GrabPipe.Instance);

        protected s_Tr_PipeV2H_OrienteDown(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The grab clip's end is ignored while the pipe hold turns the pawn toward the bar.</summary>
        public override void AnimEnd(Actor self, int Channel)
        {
        }

        /// <summary>Facing the bar below: the turn down onto it.</summary>
        [Uc("SBase.SPlayerController.s_Tr_PipeV2H_OrienteDown.TopSynchro", Sha1 = "26767e6277245a7f868c39b5ef5d5bfed6edf787")]
        [PortOf("uc:SBase.SPlayerController:s_Tr_PipeV2H_OrienteDown.TopSynchro", Spec = "spec/movement/pipe-vertical.md#to-hand-over-hand-s_tr_pipev2h_oriente--_orientedown-s_tr_pipev2h")]
        public virtual void TopSynchro(SPlayerController self)
        {
            if (self.IAmOnClient() && self.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_TR_PIPEV_H_DOWN);
                self.eventChangeState(MovementStateNames.s_Tr_PipeV2H);
            }
        }
    }

    /// <summary>The holster from a bar fire pose: the fire flags cleared and the camera mode for the hang.</summary>
    private static void HolsterOnBar(SPlayerController self, ECamMode mode)
    {
        self.bFireMode = 0;
        self.bForceExitFireMode = false;
        self.bForceFireMode = false;
        self.bTestBoutonLache = false;
        self.MovementCamera(mode);
    }
}
