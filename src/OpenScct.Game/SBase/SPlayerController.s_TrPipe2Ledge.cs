using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_TrPipe2Ledge: from a vertical pipe across to a ledge beside it (GrabLedge with
// the ledge the native found). Written from spec/movement/ledge.md (GrabLedge) and pipe-vertical.md.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_TrPipe2Ledge")]
    public class s_TrPipe2Ledge : GrabLedge
    {
        public static new readonly s_TrPipe2Ledge Instance = new(UcMeta.s_TrPipe2Ledge, GrabLedge.Instance);

        protected s_TrPipe2Ledge(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The ledge grab set-up, then the ledge points the native found become the current ones.</summary>
        [Uc("SBase.SPlayerController.s_TrPipe2Ledge.BeginState", Sha1 = "204ea4bc3d237d24382816b25c7dd6ed7412aadc")]
        [PortOf("uc:SBase.SPlayerController:s_TrPipe2Ledge.BeginState", Spec = "spec/movement/ledge.md#grabledge-simulated")]
        public override void BeginState(Actor self)
        {
            base.BeginState(self);
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient() && pc.SBPawn is { } pawn)
            {
                pawn.ZipP1 = pawn.NewZipP1;
                pawn.ZipP2 = pawn.NewZipP2;
                pawn.SendZipPToServer();
            }
        }
    }
}
