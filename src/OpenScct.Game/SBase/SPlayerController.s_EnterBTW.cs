using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_EnterBTW: the clip putting the spy's back to the wall. Written from
// spec/movement/back-to-wall.md (Entering). The class metadata gives no parent; the spec gives it the s_BTW_Base
// moves, so the C# class extends s_BTW_Base with no state parent.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_EnterBTW")]
    public class s_EnterBTW : s_BTW_Base
    {
        public static new readonly s_EnterBTW Instance = new(UcMeta.s_EnterBTW, null);

        protected s_EnterBTW(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>On the wall: standing or crouched hold.</summary>
        [Uc("SBase.SPlayerController.s_EnterBTW.AnimEnd", Sha1 = "52d968b211b12ce275ade97e03776bbed84da9ec")]
        [PortOf("uc:SBase.SPlayerController:s_EnterBTW.AnimEnd", Spec = "spec/movement/back-to-wall.md#entering-s_playerwalkingmarioprocessworldinteract")]
        public override void AnimEnd(Actor self, int Channel)
        {
            var pc = (SPlayerController)self;
            if (Channel != 0 || !pc.IAmOnClient() || pc.SBPawn is not { } pawn)
            {
                return;
            }

            if (pawn.bIsCrouched)
            {
                pawn.ChangeEnumAction(SBasePawn.SA_BTW_CR);
                pc.eventChangeState(MovementStateNames.s_BTW_down);
            }
            else
            {
                pawn.ChangeEnumAction(SBasePawn.SA_BTW_ST);
                pc.eventChangeState(MovementStateNames.s_BTW_up);
            }
        }
    }
}
