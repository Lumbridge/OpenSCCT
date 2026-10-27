using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_BTW_StandRight: peeking at the end of the wall, standing, looking round the wall end on his right (entered with the Left key).
// Written from spec/movement/back-to-wall.md (Peeking). The fire branch and the hit (PlayAnimDmg) are in
// SPlayerController.s_BTWFire.cs.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_BTW_StandRight")]
    public class s_BTW_StandRight : s_BTW
    {
        public static new readonly s_BTW_StandRight Instance = new(UcMeta.s_BTW_StandRight, s_BTW.Instance);

        protected s_BTW_StandRight(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_BTW_StandRight.BeginState", Sha1 = "fbd37d0408178cf72df556450ad72aa0395268be")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_StandRight.BeginState", Spec = "spec/movement/back-to-wall.md#peeking-s_btw_standleft--standright--crouchleft--crouchright")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.MovementCamera(ECamMode.ECM_BTWPeakLeft);
            ReinitIfTazered(pc);
        }

        /// <summary>A hit leaves the wall to the unarmed rest for the stance.</summary>
        [Uc("SBase.SPlayerController.s_BTW_StandRight.PlayAnimDmg", Sha1 = "4eee3f9dc314b4276489b8cdca7e0f555ce2ddf8")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_StandRight.PlayAnimDmg", Spec = "spec/movement/back-to-wall.md#peeking-s_btw_standleft--standright--crouchleft--crouchright")]
        public override void PlayAnimDmg(SPlayerController self, int PillTag, FVector Momentum) => self.BtwPlayAnimDmg(bKeepWeapon: false);

        [Uc("SBase.SPlayerController.s_BTW_StandRight.PlayerMove", Sha1 = "ec2feaefcd49aafb1c1a91cd1e64270eb9a45f73")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_StandRight.PlayerMove", Spec = "spec/movement/back-to-wall.md#peeking-s_btw_standleft--standright--crouchleft--crouchright")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) => self.PeekMove(DeltaTime, bLeft: false, bCrouched: false);
    }
}
