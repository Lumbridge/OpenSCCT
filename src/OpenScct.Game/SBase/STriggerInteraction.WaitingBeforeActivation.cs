using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class STriggerInteraction
{
    /// <summary>A delayed switch waits iAttenteAvantActivation seconds before firing (switch-hack.md rule 19).</summary>
    [Uc("SBase.STriggerInteraction.WaitingBeforeActivation")]
    public class WaitingBeforeActivation : UcActorState, IInitInteract
    {
        public static readonly WaitingBeforeActivation Instance = new(UcMeta.WaitingBeforeActivation, null);

        protected WaitingBeforeActivation(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.STriggerInteraction.WaitingBeforeActivation.BeginState", Sha1 = "9715ee692b052fd86c78af08fa101f770600e702")]
        [PortOf("uc:SBase.STriggerInteraction:WaitingBeforeActivation.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void BeginState(Actor self)
        {
            var entry = (STriggerInteraction)self;
            entry.SetTimer((entry.Owner as SBasicGamePlayObject)?.iAttenteAvantActivation ?? 0, false);
        }

        [Uc("SBase.STriggerInteraction.WaitingBeforeActivation.Timer", Sha1 = "ba76f8fde2a24c09430e3e214fbff92329ee84b4")]
        [PortOf("uc:SBase.STriggerInteraction:WaitingBeforeActivation.Timer", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void Timer(Actor self)
        {
            var entry = (STriggerInteraction)self;
            entry.HandleEventInfo();
            int deactivation = (entry.Owner as SBasicGamePlayObject)?.iAttenteAvantDesactivation ?? 0;
            ObjectiveNatives.MirrorState(entry, deactivation != 0 ? K.WaitingBeforeDesactivation : Name.None);
        }

        [Uc("SBase.STriggerInteraction.WaitingBeforeActivation.InitInteract", Sha1 = "df84264d331ff0aeab7c5da836840c584e374044")]
        [PortOf("uc:SBase.STriggerInteraction:WaitingBeforeActivation.InitInteract", Spec = "spec/objectives/switch-hack.md#rules")]
        public void InitInteract(SInteractObject self, SPlayerController Instigator, int SubInteraction) =>
            ((STriggerInteraction)self).RefuseUse(Instigator, SubInteraction);
    }
}
