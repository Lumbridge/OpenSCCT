using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Movement;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_Coop_S_Goto: the climber's approach to the co-op spot, given up after 8 s or on a
// sticky shock. Written from spec/movement/coop.md (short ladder, climber, rule 2).
public partial class SPlayerController
{
    /// <remarks>PawnTakeDamage is the combat port's (by state name).</remarks>
    [Uc("SBase.SPlayerController.s_Coop_S_Goto")]
    public class s_Coop_S_Goto : s_Coop_S_Base, IClientStickyShock
    {
        public static new readonly s_Coop_S_Goto Instance = new(UcMeta.s_Coop_S_Goto, s_Coop_S_Base.Instance);

        protected s_Coop_S_Goto(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>The controlling side arms the approach timeout.</summary>
        [Uc("SBase.SPlayerController.s_Coop_S_Goto.BeginState", Sha1 = "33d00461b85c965a51e6ffaa43369f9524eae3a8")]
        [PortOf("uc:SBase.SPlayerController:s_Coop_S_Goto.BeginState", Spec = "spec/movement/coop.md#short-ladder-climber")]
        public override void BeginState(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient())
            {
                pc.SetTimer(MovementK.CoopApproachTimeout, false);
            }

            base.BeginState(self);
        }

        /// <summary>The approach took too long: back to walking.</summary>
        [Uc("SBase.SPlayerController.s_Coop_S_Goto.Timer", Sha1 = "dd8290a796d5dafad63a37bab90e3cb6fd2b1dd0")]
        [PortOf("uc:SBase.SPlayerController:s_Coop_S_Goto.Timer", Spec = "spec/movement/coop.md#short-ladder-climber")]
        public override void Timer(Actor self)
        {
            var pc = (SPlayerController)self;
            if (pc.IAmOnClient())
            {
                pc.GotoWaitingState();
            }
        }

        /// <summary>A sticky shock stops the approach.</summary>
        [Uc("SBase.SPlayerController.s_Coop_S_Goto.ClientStickyShock", Sha1 = "b8d084616975ac42e3199e6cb1a451d68c5ad7d1")]
        [PortOf("uc:SBase.SPlayerController:s_Coop_S_Goto.ClientStickyShock", Spec = "spec/combat/shock-and-taser.md#clientstickyshockpilltag-bplayanim-owner-in-the-owners-current-state")]
        public virtual void ClientStickyShock(SPlayerController self, int PillTag, bool bPlayAnim)
        {
            self.GlobalClientStickyShock(PillTag, bPlayAnim);
            self.GotoWaitingState();
        }
    }
}
