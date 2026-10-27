using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class STriggerInteraction
{
    /// <summary>A panel hack runs (switch-hack.md rule 15): the event fires only on success.</summary>
    [Uc("SBase.STriggerInteraction.s_Hacking")]
    public class s_Hacking : s_Maintain_Use
    {
        public static new readonly s_Hacking Instance = new(UcMeta.s_Hacking, s_Maintain_Use.Instance);

        protected s_Hacking(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.STriggerInteraction.s_Hacking.EndState", Sha1 = "7a1b0ab22114f9aba8806ec71ce87c1a1c3e6cc6")]
        [PortOf("uc:SBase.STriggerInteraction:s_Hacking.EndState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void EndState(Actor self)
        {
            var entry = (STriggerInteraction)self;
            entry.MinTriggerTime = 0f;
            entry.AddRemoveActorOnClientHud(null, false);
            base.EndState(self);
        }

        [Uc("SBase.STriggerInteraction.s_Hacking.PostInteract", Sha1 = "b9a8ca53f16901949f77440fdfa3dfe6aaab76aa")]
        [PortOf("uc:SBase.STriggerInteraction:s_Hacking.PostInteract", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void PostInteract(SInteractObject self, SPlayerController Instigator, int SubInteraction)
        {
            var entry = (STriggerInteraction)self;
            if (SubInteraction == K.SubSuccess)
            {
                entry.HandleEventInfo();
            }

            ObjectiveNatives.ChangeState(entry, Name.None);
        }
    }
}
