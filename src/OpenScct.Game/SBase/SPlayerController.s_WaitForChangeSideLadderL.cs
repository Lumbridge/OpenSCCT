using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController states s_WaitForChangeSideLadderL and s_WaitForChangeSideLadderR: the hand comes off the
// ladder before the swing to its far side. Written from spec/movement/ladder.md (changing side).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_WaitForChangeSideLadderL")]
    public class s_WaitForChangeSideLadderL : s_LadderBase, IPlayerMove
    {
        public static new readonly s_WaitForChangeSideLadderL Instance = new(UcMeta.s_WaitForChangeSideLadderL, s_LadderBase.Instance);

        protected s_WaitForChangeSideLadderL(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_WaitForChangeSideLadderL.AnimEnd", Sha1 = "9091f57dcfd8388891d766ea80a23a2ff17dd1a8")]
        [PortOf("uc:SBase.SPlayerController:s_WaitForChangeSideLadderL.AnimEnd", Spec = "spec/movement/ladder.md#changing-side")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_CHANGESIDELADDER_L);
                pc.eventChangeState(MovementStateNames.s_ChangeSideLadder);
            }
        }

        [Uc("SBase.SPlayerController.s_WaitForChangeSideLadderL.PlayerMove", Sha1 = "783748ad1505aefe272c53d1fa414a6e5cc3819f")]
        [PortOf("uc:SBase.SPlayerController:s_WaitForChangeSideLadderL.PlayerMove", Spec = "spec/movement/ladder.md#changing-side")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime) => self.LadderViewMove(DeltaTime, default);
    }

    [Uc("SBase.SPlayerController.s_WaitForChangeSideLadderR")]
    public class s_WaitForChangeSideLadderR : s_LadderBase, IPlayerMove
    {
        public static new readonly s_WaitForChangeSideLadderR Instance = new(UcMeta.s_WaitForChangeSideLadderR, s_LadderBase.Instance);

        protected s_WaitForChangeSideLadderR(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_WaitForChangeSideLadderR.AnimEnd", Sha1 = "5041c8f9b8f8b02d7e97d24e40a5832523713807")]
        [PortOf("uc:SBase.SPlayerController:s_WaitForChangeSideLadderR.AnimEnd", Spec = "spec/movement/ladder.md#changing-side")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_CHANGESIDELADDER_R);
                pc.eventChangeState(MovementStateNames.s_ChangeSideLadder);
            }
        }

        [Uc("SBase.SPlayerController.s_WaitForChangeSideLadderR.PlayerMove", Sha1 = "027d2a2763d284f41f47a5034a2083287cb8f255")]
        [PortOf("uc:SBase.SPlayerController:s_WaitForChangeSideLadderR.PlayerMove", Spec = "spec/movement/ladder.md#changing-side")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime) => self.LadderViewMove(DeltaTime, default);
    }

    [Uc("SBase.SPlayerController.s_ChangeSideLadder")]
    public class s_ChangeSideLadder : s_LadderBase, IPlayerMove
    {
        public static new readonly s_ChangeSideLadder Instance = new(UcMeta.s_ChangeSideLadder, s_LadderBase.Instance);

        protected s_ChangeSideLadder(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The swing ended on the far side: at rest on a rung there.</summary>
        [Uc("SBase.SPlayerController.s_ChangeSideLadder.AnimEnd", Sha1 = "1227fb4ee97c184299b53a3ac3d6c07d097c88f7")]
        [PortOf("uc:SBase.SPlayerController:s_ChangeSideLadder.AnimEnd", Spec = "spec/movement/ladder.md#changing-side")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_AFTER_CHANGESIDELADDER);
                pc.MovementCamera(ECamMode.ECM_OnLadder);
                pc.eventChangeState(MovementStateNames.s_OnStopLadder);
            }
        }

        [Uc("SBase.SPlayerController.s_ChangeSideLadder.PlayerMove", Sha1 = "d2bdf2a424b2503e5ebd35fb287ce7e0a70d5144")]
        [PortOf("uc:SBase.SPlayerController:s_ChangeSideLadder.PlayerMove", Spec = "spec/movement/ladder.md#changing-side")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime) => self.LadderViewMove(DeltaTime, default);
    }

    [Uc("SBase.SPlayerController.s_OnStopLadder")]
    public class s_OnStopLadder : s_LadderBase, IPlayerMove
    {
        public static new readonly s_OnStopLadder Instance = new(UcMeta.s_OnStopLadder, s_LadderBase.Instance);

        protected s_OnStopLadder(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>At rest after a side swing or a catch: Up climbs on, Down goes back to climbing, Left and Right swing again.</summary>
        [Uc("SBase.SPlayerController.s_OnStopLadder.PlayerMove", Sha1 = "a4fdfe94b829441f87c2796a985471b70348eee2")]
        [PortOf("uc:SBase.SPlayerController:s_OnStopLadder.PlayerMove", Spec = "spec/movement/ladder.md#changing-side")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            if (self.SBPawn is not { } pawn)
            {
                return;
            }

            self.MercLadderFirstPerson();
            self.bPressedJump = false;
            if (self.bCrouchWithRoll)
            {
                JumpFromLadder(self);
            }
            else
            {
                if ((self.iMove & cMoveUp) != 0 && !self.CheckCeilingForLadder())
                {
                    self.iMove -= cMoveUp;
                }

                if ((self.iMove & cMoveUp) != 0)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_LADDER_UP2);
                    self.eventChangeState(MovementStateNames.s_LadderUp2);
                }
                else if ((self.iMove & cMoveDown) != 0)
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_BACK_LADDER_TOP);
                    self.eventChangeState(MovementStateNames.s_Back_LadderTop);
                }
                else if ((self.iMove & cMoveLeft) != 0)
                {
                    if (self.CheckRoomToChangeSideLadder(true))
                    {
                        pawn.ChangeEnumAction(SBasePawn.SA_CHANGESIDELADDER_L);
                        self.eventChangeState(MovementStateNames.s_ChangeSideLadder);
                    }
                }
                else if ((self.iMove & cMoveRight) != 0 && self.CheckRoomToChangeSideLadder(false))
                {
                    pawn.ChangeEnumAction(SBasePawn.SA_CHANGESIDELADDER_R);
                    self.eventChangeState(MovementStateNames.s_ChangeSideLadder);
                }
            }

            self.LadderView(DeltaTime);
            self.SendMove(DeltaTime, default);
        }
    }

    [Uc("SBase.SPlayerController.s_LadderUp2")]
    public class s_LadderUp2 : s_LadderBase, IPlayerMove
    {
        public static new readonly s_LadderUp2 Instance = new(UcMeta.s_LadderUp2, null);

        protected s_LadderUp2(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_LadderUp2.AnimEnd", Sha1 = "6e39c7a89a07c8f32f3186befd65e5945e1b83d0")]
        [PortOf("uc:SBase.SPlayerController:s_LadderUp2.AnimEnd", Spec = "spec/movement/ladder.md#changing-side")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_LADDER);
                pc.eventChangeState(MovementStateNames.s_OnLadder);
            }
        }

        /// <summary>The first step up from rest: climbing at 126 until the clip ends.</summary>
        [Uc("SBase.SPlayerController.s_LadderUp2.PlayerMove", Sha1 = "ca621b02f33342e38770959e6d3315c8a6c41b46")]
        [PortOf("uc:SBase.SPlayerController:s_LadderUp2.PlayerMove", Spec = "spec/movement/ladder.md#changing-side")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.MercLadderFirstPerson();
            self.LadderView(DeltaTime);
            self.bPressedJump = false;
            if (self.bCrouchWithRoll)
            {
                JumpFromLadder(self);
            }

            self.SendMove(DeltaTime, new FVector(0f, 0f, MovementK.SpeedForLadder));
        }
    }

    [Uc("SBase.SPlayerController.s_Back_LadderTop")]
    public class s_Back_LadderTop : s_LadderBase, IPlayerMove
    {
        public static new readonly s_Back_LadderTop Instance = new(UcMeta.s_Back_LadderTop, s_LadderBase.Instance);

        protected s_Back_LadderTop(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Back_LadderTop.AnimEnd", Sha1 = "941702963345d370244c73f3b8a9217caa830644")]
        [PortOf("uc:SBase.SPlayerController:s_Back_LadderTop.AnimEnd", Spec = "spec/movement/ladder.md#changing-side")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel == 0 && pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_ON_LADDER);
                pc.eventChangeState(MovementStateNames.s_OnLadder);
            }
        }

        /// <summary>Back down onto the climb: crouch lets go, Use with Down slides.</summary>
        [Uc("SBase.SPlayerController.s_Back_LadderTop.PlayerMove", Sha1 = "c1ce441435a8464d34c4208927054e2f7ee941a0")]
        [PortOf("uc:SBase.SPlayerController:s_Back_LadderTop.PlayerMove", Spec = "spec/movement/ladder.md#changing-side")]
        public virtual void PlayerMove(SPlayerController self, float DeltaTime)
        {
            self.MercLadderFirstPerson();
            self.bPressedJump = false;
            if (self.bCrouchWithRoll)
            {
                JumpFromLadder(self);
            }
            else if (self.bUsing == 1 && (self.iMove & cMoveDown) != 0 && self.SBPawn is { } pawn)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_SPEEDLADDER);
                self.eventChangeState(MovementStateNames.s_SpeedLadder);
            }

            self.LadderView(DeltaTime);
            self.SendMove(DeltaTime, default);
        }
    }
}
