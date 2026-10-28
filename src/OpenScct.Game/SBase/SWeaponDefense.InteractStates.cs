using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Equipment;
using OpenScct.Provenance;
using K = OpenScct.Game.Equipment.K;

namespace OpenScct.Game.SBase;

// SBase.SWeaponDefense view-model states of the hand moves (spec/equipment/merc-rifle.md rule 25): the button press
// while the merc works an object (s_Interacting) and the four clips of a wall mine (s_DropMine_Grab, _Loop, s_DropMine,
// _UnGrab, _Abort). The laser goes off for the clip and back on at its end, and the rifle returns to s_Selected. A grab
// during a mine clip swaps it for the grab clip at once.
public partial class SWeaponDefense
{
    private static readonly Name DNAME_s_DropMine_Loop = new("s_DropMine_Loop");

    /// <summary>The grab clip from a hand move: laser back on, the idle state, the grab clip on channel 0.</summary>
    private void HandMoveGrabbed()
    {
        OwnerPC?.RallumeLaserEteint();
        GotoState(CNAME_s_Selected);
        PreviousState = CNAME_s_Selected;
        ViewAnim.Play(MVTCHANNEL, ViewModelClips.FirstGrbdStBg2, K.FirstGrbdStBg2);
    }

    /// <summary>The end of a hand clip on channel 0: the idle loop, laser back on, the idle state.</summary>
    private void HandMoveEnded()
    {
        AnimateWaiting();
        OwnerPC?.RallumeLaserEteint();
        PreviousState = CNAME_s_Selected;
        GotoState(CNAME_s_Selected);
    }

    /// <summary>s_Interacting: the button press clip while the merc works an object.</summary>
    [Uc("SBase.SWeaponDefense.s_Interacting")]
    public class s_Interacting : UcActorState
    {
        public static readonly s_Interacting Instance = new(UcMeta.s_Interacting, null);

        protected s_Interacting(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_Interacting.BeginState", Sha1 = "03550602b2f57cb4f891ffe6a65268c98eb6c71c")]
        [PortOf("uc:SBase.SWeaponDefense:s_Interacting.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            w.OwnerPC?.EteintLaser();
            w.ViewAnim.Play(MVTCHANNEL, ViewModelClips.FirstButtStFd2, K.FirstButtStFd2);
        }

        /// <summary>Any clip end turns the laser back on; the button clip's end returns to the idle.</summary>
        [Uc("SBase.SWeaponDefense.s_Interacting.AnimEnd", Sha1 = "645c9bad3afa69950207e7e33ad855e1a1293ff7")]
        [PortOf("uc:SBase.SWeaponDefense:s_Interacting.AnimEnd", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var w = (SWeaponDefense)self;
            w.OwnerPC?.RallumeLaserEteint();
            if (Channel == MVTCHANNEL)
            {
                w.AnimateWaiting();
                w.PreviousState = CNAME_s_Selected;
                w.GotoState(w.PreviousState);
            }

            w.AnimEnd(Channel);
        }
    }

    /// <summary>s_DropMine_Grab: the mine comes out (FirstMineStBg2), then the hold loop.</summary>
    [Uc("SBase.SWeaponDefense.s_DropMine_Grab")]
    public class s_DropMine_Grab : UcActorState, IAnimateUnDraw_GRAB
    {
        public static readonly s_DropMine_Grab Instance = new(UcMeta.s_DropMine_Grab, null);

        protected s_DropMine_Grab(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_DropMine_Grab.BeginState", Sha1 = "40e044c872462cf3e95340642164c69e3f55d7a0")]
        [PortOf("uc:SBase.SWeaponDefense:s_DropMine_Grab.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            w.OwnerPC?.EteintLaser();
            w.ViewAnim.Play(MVTCHANNEL, ViewModelClips.FirstMineStBg2, K.FirstMineStBg2);
        }

        [Uc("SBase.SWeaponDefense.s_DropMine_Grab.AnimEnd", Sha1 = "c4b76995f0b5c5a140dc6f97982cea0cd6683044")]
        [PortOf("uc:SBase.SWeaponDefense:s_DropMine_Grab.AnimEnd", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var w = (SWeaponDefense)self;
            if (Channel == MVTCHANNEL)
            {
                w.GotoState(DNAME_s_DropMine_Loop);
            }

            w.AnimEnd(Channel);
        }

        [Uc("SBase.SWeaponDefense.s_DropMine_Grab.AnimateUnDraw_GRAB", Sha1 = "4457c5a1b70cbacfaca70dc14f7eae7917ce0eb9")]
        [PortOf("uc:SBase.SWeaponDefense:s_DropMine_Grab.AnimateUnDraw_GRAB", Spec = "spec/equipment/merc-rifle.md#rules")]
        public void AnimateUnDraw_GRAB(SWeaponDefense self) => self.HandMoveGrabbed();
    }

    /// <summary>s_DropMine_UnGrab: the mine goes back (FirstMineStBg2 backward), then the idle.</summary>
    [Uc("SBase.SWeaponDefense.s_DropMine_UnGrab")]
    public class s_DropMine_UnGrab : UcActorState, IAnimateUnDraw_GRAB
    {
        public static readonly s_DropMine_UnGrab Instance = new(UcMeta.s_DropMine_UnGrab, null);

        protected s_DropMine_UnGrab(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_DropMine_UnGrab.BeginState", Sha1 = "05bf8941ca1cf48ad8679e42f9e778811dd6f42d")]
        [PortOf("uc:SBase.SWeaponDefense:s_DropMine_UnGrab.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            w.OwnerPC?.EteintLaser();
            w.ViewAnim.Play(MVTCHANNEL, ViewModelClips.FirstMineStBg2, K.FirstMineStBg2, Backward: true);
        }

        [Uc("SBase.SWeaponDefense.s_DropMine_UnGrab.AnimEnd", Sha1 = "22fe61c92bc20873a62bf59b22b5e98d2ec2fa75")]
        [PortOf("uc:SBase.SWeaponDefense:s_DropMine_UnGrab.AnimEnd", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var w = (SWeaponDefense)self;
            if (Channel == MVTCHANNEL)
            {
                w.HandMoveEnded();
            }

            w.AnimEnd(Channel);
        }

        [Uc("SBase.SWeaponDefense.s_DropMine_UnGrab.AnimateUnDraw_GRAB", Sha1 = "4457c5a1b70cbacfaca70dc14f7eae7917ce0eb9")]
        [PortOf("uc:SBase.SWeaponDefense:s_DropMine_UnGrab.AnimateUnDraw_GRAB", Spec = "spec/equipment/merc-rifle.md#rules")]
        public void AnimateUnDraw_GRAB(SWeaponDefense self) => self.HandMoveGrabbed();
    }

    /// <summary>s_DropMine_Loop: the mine held against the wall (FirstMineStNt2 loops) while the menu is up.</summary>
    [Uc("SBase.SWeaponDefense.s_DropMine_Loop")]
    public class s_DropMine_Loop : UcActorState, IAnimateUnDraw_GRAB
    {
        public static readonly s_DropMine_Loop Instance = new(UcMeta.s_DropMine_Loop, null);

        protected s_DropMine_Loop(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_DropMine_Loop.BeginState", Sha1 = "9da81b9145be66bf54fe56fdc0cc8da2c14454c0")]
        [PortOf("uc:SBase.SWeaponDefense:s_DropMine_Loop.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            w.OwnerPC?.EteintLaser();
            w.ViewAnim.Loop(MVTCHANNEL, ViewModelClips.FirstMineStNt2);
        }

        [Uc("SBase.SWeaponDefense.s_DropMine_Loop.AnimateUnDraw_GRAB", Sha1 = "4457c5a1b70cbacfaca70dc14f7eae7917ce0eb9")]
        [PortOf("uc:SBase.SWeaponDefense:s_DropMine_Loop.AnimateUnDraw_GRAB", Spec = "spec/equipment/merc-rifle.md#rules")]
        public void AnimateUnDraw_GRAB(SWeaponDefense self) => self.HandMoveGrabbed();
    }

    /// <summary>s_DropMine: the mine is set on the wall (FirstMineStEd2), then the idle.</summary>
    [Uc("SBase.SWeaponDefense.s_DropMine")]
    public class s_DropMine : UcActorState, IAnimateUnDraw_GRAB
    {
        public static readonly s_DropMine Instance = new(UcMeta.s_DropMine, null);

        protected s_DropMine(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_DropMine.BeginState", Sha1 = "2d30e7a81bb168923828601427d23ba738ec534d")]
        [PortOf("uc:SBase.SWeaponDefense:s_DropMine.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            w.OwnerPC?.EteintLaser();
            w.ViewAnim.Play(MVTCHANNEL, ViewModelClips.FirstMineStEd2, K.FirstMineStEd2);
        }

        [Uc("SBase.SWeaponDefense.s_DropMine.AnimEnd", Sha1 = "1f00e8e393945a7302c85ffda6bcf661fff4efad")]
        [PortOf("uc:SBase.SWeaponDefense:s_DropMine.AnimEnd", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var w = (SWeaponDefense)self;
            if (Channel == MVTCHANNEL)
            {
                w.HandMoveEnded();
            }

            w.AnimEnd(Channel);
        }

        [Uc("SBase.SWeaponDefense.s_DropMine.AnimateUnDraw_GRAB", Sha1 = "4457c5a1b70cbacfaca70dc14f7eae7917ce0eb9")]
        [PortOf("uc:SBase.SWeaponDefense:s_DropMine.AnimateUnDraw_GRAB", Spec = "spec/equipment/merc-rifle.md#rules")]
        public void AnimateUnDraw_GRAB(SWeaponDefense self) => self.HandMoveGrabbed();
    }

    /// <summary>s_DropMine_Abort: the idle loop at once and back to the state before.</summary>
    [Uc("SBase.SWeaponDefense.s_DropMine_Abort")]
    public class s_DropMine_Abort : UcActorState, IAnimateUnDraw_GRAB
    {
        public static readonly s_DropMine_Abort Instance = new(UcMeta.s_DropMine_Abort, null);

        protected s_DropMine_Abort(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SWeaponDefense.s_DropMine_Abort.BeginState", Sha1 = "5809d15ece9791a26ba5b5b6a3f5cd8dbc03beb4")]
        [PortOf("uc:SBase.SWeaponDefense:s_DropMine_Abort.BeginState", Spec = "spec/equipment/merc-rifle.md#rules")]
        public override void BeginState(Actor self)
        {
            var w = (SWeaponDefense)self;
            w.AnimateWaiting();
            w.GotoState(w.PreviousState.IsNone ? CNAME_s_Selected : w.PreviousState);
        }

        [Uc("SBase.SWeaponDefense.s_DropMine_Abort.AnimateUnDraw_GRAB", Sha1 = "4457c5a1b70cbacfaca70dc14f7eae7917ce0eb9")]
        [PortOf("uc:SBase.SWeaponDefense:s_DropMine_Abort.AnimateUnDraw_GRAB", Spec = "spec/equipment/merc-rifle.md#rules")]
        public void AnimateUnDraw_GRAB(SWeaponDefense self) => self.HandMoveGrabbed();
    }
}
