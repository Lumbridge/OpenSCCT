using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_BTW_CrouchLeft: peeking at the end of the wall, crouched, looking round the wall end on his left (entered with the Right key).
// Written from spec/movement/back-to-wall.md (Peeking). The fire branch and the hit (PlayAnimDmg) are in
// SPlayerController.s_BTWFire.cs.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_BTW_CrouchLeft")]
    public class s_BTW_CrouchLeft : s_BTW
    {
        public static new readonly s_BTW_CrouchLeft Instance = new(UcMeta.s_BTW_CrouchLeft, s_BTW.Instance);

        protected s_BTW_CrouchLeft(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_BTW_CrouchLeft.BeginState", Sha1 = "06298a73d38601782024a8900cd254cd21124f15")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_CrouchLeft.BeginState", Spec = "spec/movement/back-to-wall.md#peeking-s_btw_standleft--standright--crouchleft--crouchright")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.MovementCamera(ECamMode.ECM_BTWPeakRight);
            ReinitIfTazered(pc);
        }

        /// <summary>A hit leaves the wall to the unarmed rest for the stance.</summary>
        [Uc("SBase.SPlayerController.s_BTW_CrouchLeft.PlayAnimDmg", Sha1 = "ff7cbfc180ec1d7aed1af2fdf9040c189347a0de")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_CrouchLeft.PlayAnimDmg", Spec = "spec/movement/back-to-wall.md#peeking-s_btw_standleft--standright--crouchleft--crouchright")]
        public override void PlayAnimDmg(SPlayerController self, int PillTag, FVector Momentum) => self.BtwPlayAnimDmg(bKeepWeapon: false);

        [Uc("SBase.SPlayerController.s_BTW_CrouchLeft.PlayerMove", Sha1 = "0d7248e073a071199103d70c2c53bfce65525aa6")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_CrouchLeft.PlayerMove", Spec = "spec/movement/back-to-wall.md#peeking-s_btw_standleft--standright--crouchleft--crouchright")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) => self.PeekMove(DeltaTime, bLeft: true, bCrouched: true);
    }
}
