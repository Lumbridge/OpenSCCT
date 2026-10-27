using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class STriggerInteraction
{
    /// <summary>After a delayed activation, waits iAttenteAvantDesactivation seconds and fires again (rule 19).</summary>
    [Uc("SBase.STriggerInteraction.WaitingBeforeDesactivation")]
    public class WaitingBeforeDesactivation : UcActorState, IInitInteract
    {
        public static readonly WaitingBeforeDesactivation Instance = new(UcMeta.WaitingBeforeDesactivation, null);

        protected WaitingBeforeDesactivation(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.STriggerInteraction.WaitingBeforeDesactivation.BeginState", Sha1 = "151cbd159c499c10acf312d8735bddb42b7bc72c")]
        [PortOf("uc:SBase.STriggerInteraction:WaitingBeforeDesactivation.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var entry = (STriggerInteraction)self;
            entry.SetTimer((entry.Owner as SBasicGamePlayObject)?.iAttenteAvantDesactivation ?? 0, false);
        }

        [Uc("SBase.STriggerInteraction.WaitingBeforeDesactivation.Timer", Sha1 = "2255a2fd163f556239d09bef219dc21e511231b7")]
        [PortOf("uc:SBase.STriggerInteraction:WaitingBeforeDesactivation.Timer", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void Timer(Actor self)
        {
            var entry = (STriggerInteraction)self;
            entry.HandleEventInfo();
            ObjectiveNatives.MirrorState(entry, Name.None);
        }

        [Uc("SBase.STriggerInteraction.WaitingBeforeDesactivation.InitInteract", Sha1 = "13e0bfa4b7847cae41ec475c2087677e05faf66d")]
        [PortOf("uc:SBase.STriggerInteraction:WaitingBeforeDesactivation.InitInteract", Spec = "spec/objectives/switch-hack.md#rules")]
        public void InitInteract(SInteractObject self, SPlayerController Instigator, int SubInteraction) =>
            ((STriggerInteraction)self).RefuseUse(Instigator, SubInteraction);
    }
}
