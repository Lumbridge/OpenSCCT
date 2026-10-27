using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_ShortLadder_S_Base: the parent of the short-ladder climber's jump. Written from
// spec/movement/coop.md (short ladder, climber).
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_ShortLadder_S_Base")]
    public class s_ShortLadder_S_Base : s_Coop_S_Base, IPlayerTick, ICanUncrouchAndUndraw
    {
        public static new readonly s_ShortLadder_S_Base Instance = new(UcMeta.s_ShortLadder_S_Base, s_Coop_S_Base.Instance);

        protected s_ShortLadder_S_Base(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        /// <summary>No fire mode, crouch or roll during the climb; then the global tick.</summary>
        [Uc("SBase.SPlayerController.s_ShortLadder_S_Base.PlayerTick", Sha1 = "7c18f67d1351a3f4a26112e69df138c05d714200")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_S_Base.PlayerTick", Spec = "spec/movement/coop.md#short-ladder-climber")]
        public virtual void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.bFireMode = 0;
            pc.bCrouch = 0;
            pc.bRoll = 0;
            pc.PlayerTick(DeltaTime);
        }

        [Uc("SBase.SPlayerController.s_ShortLadder_S_Base.CanUncrouchAndUndraw", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_ShortLadder_S_Base.CanUncrouchAndUndraw", Spec = "spec/movement/coop.md#short-ladder-climber")]
        public virtual bool CanUncrouchAndUndraw(SPlayerController self) => true;
    }
}
