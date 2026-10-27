using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SBasicGamePlayObject
{
    /// <summary>
    /// Locked by chaff or a sticky shot: a malfunction message on entry, a new lock only restarts the timer, and the
    /// timer returns to the remembered state.
    /// </summary>
    [Uc("SBase.SBasicGamePlayObject.s_LockedByChaff")]
    public class s_LockedByChaff : UcActorState, ISetLockedByChaff
    {
        public static readonly s_LockedByChaff Instance = new(UcMeta.s_LockedByChaff, null);

        protected s_LockedByChaff(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SBasicGamePlayObject.s_LockedByChaff.BeginState", Sha1 = "fe87779e4d31638929c3b13cafbec99e7d8d0e46")]
        [PortOf("uc:SBase.SBasicGamePlayObject:s_LockedByChaff.BeginState", Spec = "spec/devices/gameplay-object.md#chaff-lock")]
        public override void BeginState(Actor self) => ((SBasicGamePlayObject)self).Malfunction();

        [Uc("SBase.SBasicGamePlayObject.s_LockedByChaff.EndState", Sha1 = "79c4e98e44bf47754d1842eff25cab69f0579740")]
        [PortOf("uc:SBase.SBasicGamePlayObject:s_LockedByChaff.EndState", Spec = "spec/devices/gameplay-object.md#chaff-lock")]
        public override void EndState(Actor self) => ((SBasicGamePlayObject)self).ChaffLockPreviousState = Name.None;

        [Uc("SBase.SBasicGamePlayObject.s_LockedByChaff.Tick", Sha1 = "23aff4eecf0e2e62053f2e78cf6e28d2d332361e")]
        [PortOf("uc:SBase.SBasicGamePlayObject:s_LockedByChaff.Tick", Spec = "spec/devices/gameplay-object.md#chaff-lock")]
        public override void Tick(Actor self, float DeltaTime)
        {
        }

        [Uc("SBase.SBasicGamePlayObject.s_LockedByChaff.SetLockedByChaff", Sha1 = "82b0cd3d473fd8e7b15a6809bef56a8c958b5d98")]
        [PortOf("uc:SBase.SBasicGamePlayObject:s_LockedByChaff.SetLockedByChaff", Spec = "spec/devices/gameplay-object.md#chaff-lock")]
        public void SetLockedByChaff(SBasicGamePlayObject self, float Length)
        {
            if (self.bCanBeChaffed)
            {
                self.SetTimer(Length, false);
                self.ERControl?.LockChaff(Length);
            }
        }

        [Uc("SBase.SBasicGamePlayObject.s_LockedByChaff.Timer", Sha1 = "85aed8544109c027507849686ebb9e4943e8b406")]
        [PortOf("uc:SBase.SBasicGamePlayObject:s_LockedByChaff.Timer", Spec = "spec/devices/gameplay-object.md#chaff-lock")]
        public override void Timer(Actor self) => self.GotoState(((SBasicGamePlayObject)self).ChaffLockPreviousState);
    }
}
