using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_BTW_StandLeft: peeking at the end of the wall, standing, looking round the wall end on his left (entered with the Right key).
// Written from spec/movement/back-to-wall.md (Peeking). Not ported here: PlayAnimDmg (combat port) and the fire
// mode branch (the s_BTWFire* states belong to the weapon port).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_BTW_StandLeft")]
    public class s_BTW_StandLeft : s_BTW
    {
        public static new readonly s_BTW_StandLeft Instance = new(UcMeta.s_BTW_StandLeft, s_BTW.Instance);

        protected s_BTW_StandLeft(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_BTW_StandLeft.BeginState", Sha1 = "06298a73d38601782024a8900cd254cd21124f15")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_StandLeft.BeginState", Spec = "spec/movement/back-to-wall.md#peeking-s_btw_standleft--standright--crouchleft--crouchright")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            pc.MovementCamera(ECamMode.ECM_BTWPeakRight);
            ReinitIfTazered(pc);
        }

        [Uc("SBase.SPlayerController.s_BTW_StandLeft.PlayerMove", Sha1 = "0d463878fe8ea4aad1e125c684e6d70c665d136f")]
        [PortOf("uc:SBase.SPlayerController:s_BTW_StandLeft.PlayerMove", Spec = "spec/movement/back-to-wall.md#peeking-s_btw_standleft--standright--crouchleft--crouchright")]
        public override void PlayerMove(SPlayerController self, float DeltaTime) => self.PeekMove(DeltaTime, bLeft: true, bCrouched: false);
    }
}
