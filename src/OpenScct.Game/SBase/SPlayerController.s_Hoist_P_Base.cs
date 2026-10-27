using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

// SBase.SPlayerController state s_Hoist_P_Base: the hanging helper of a hoist climb; the hang (physics and ledge
// events) stays OnLedge's. Written from spec/movement/coop.md (hoist, helper).
//
// The source declares it as extending OnLedge; the generated state metadata lists no super state, so the state
// chain follows the metadata while the C# class keeps OnLedge's behaviour.
public partial class SPlayerController
{
    [Uc("SBase.SPlayerController.s_Hoist_P_Base")]
    public class s_Hoist_P_Base : OnLedge, IPlayerTick, ICanSwitchGoggleManually, ICanActivateLaser, ICanShortLadderHelp,
        ICanIUseBinocular, ICanUncrouchAndUndraw
    {
        public static new readonly s_Hoist_P_Base Instance = new(UcMeta.s_Hoist_P_Base, null);

        protected s_Hoist_P_Base(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SPlayerController.s_Hoist_P_Base.CanUseCam", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_Base.CanUseCam", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public override bool CanUseCam(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_Hoist_P_Base.CanSwitchGoggleManually", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_Base.CanSwitchGoggleManually", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public virtual bool CanSwitchGoggleManually(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_Hoist_P_Base.CanActivateLaser", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_Base.CanActivateLaser", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public virtual bool CanActivateLaser(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_Hoist_P_Base.CanShortLadderHelp", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_Base.CanShortLadderHelp", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public virtual bool CanShortLadderHelp(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_Hoist_P_Base.CanIUseBinocular", Sha1 = "8567be1d106d88e612e9866eb7503ad103460bfb")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_Base.CanIUseBinocular", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public virtual bool CanIUseBinocular(SPlayerController self) => false;

        [Uc("SBase.SPlayerController.s_Hoist_P_Base.CanUncrouchAndUndraw", Sha1 = "a40381ccfdf342092554b33966344721f68fd6c2")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_Base.CanUncrouchAndUndraw", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public virtual bool CanUncrouchAndUndraw(SPlayerController self) => true;

        /// <summary>No fire mode, crouch or roll while helping; then the global tick.</summary>
        [Uc("SBase.SPlayerController.s_Hoist_P_Base.PlayerTick", Sha1 = "98885b864b905a637de35b335ab1388595b19a47")]
        [PortOf("uc:SBase.SPlayerController:s_Hoist_P_Base.PlayerTick", Spec = "spec/movement/coop.md#hoist-helper-hanging")]
        public virtual void PlayerTick(PlayerController self, float DeltaTime)
        {
            var pc = (SPlayerController)self;
            pc.bFireMode = 0;
            pc.bCrouch = 0;
            pc.bRoll = 0;
            pc.PlayerTick(DeltaTime);
        }
    }
}
