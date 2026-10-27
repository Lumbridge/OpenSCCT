using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Game.Objectives;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class STriggerInteraction
{
    // SInteractObject.InitInteract, reached from the states that refuse a second user.
    internal void RefuseUse(SPlayerController Instigator, int SubInteraction) => base.InitInteract(Instigator, SubInteraction);

    /// <summary>A hold runs on the entry (switch-hack.md rule 18): the event fires when it starts and when it ends.</summary>
    [Uc("SBase.STriggerInteraction.s_Maintain_Use")]
    public class s_Maintain_Use : UcActorState, IInitInteract, IPostInteract
    {
        public static readonly s_Maintain_Use Instance = new(UcMeta.s_Maintain_Use, null);

        protected s_Maintain_Use(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.STriggerInteraction.s_Maintain_Use.BeginState", Sha1 = "8a55000c35864727c5fc5837fa95fbf6da943022")]
        [PortOf("uc:SBase.STriggerInteraction:s_Maintain_Use.BeginState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void BeginState(Actor self) => ((STriggerInteraction)self).bInUse = true;

        [Uc("SBase.STriggerInteraction.s_Maintain_Use.EndState", Sha1 = "13b160021e3f5844fb6ede8b53495926f9b0c57e")]
        [PortOf("uc:SBase.STriggerInteraction:s_Maintain_Use.EndState", Spec = "spec/objectives/switch-hack.md#rules")]
        public override void EndState(Actor self) => ((STriggerInteraction)self).bInUse = false;

        /// <summary>A second user while it runs gets the base refusal.</summary>
        [Uc("SBase.STriggerInteraction.s_Maintain_Use.InitInteract", Sha1 = "8fc38e8c3ff6e392c9522b3f8a28bee58ead6d57")]
        [PortOf("uc:SBase.STriggerInteraction:s_Maintain_Use.InitInteract", Spec = "spec/objectives/switch-hack.md#rules")]
        public virtual void InitInteract(SInteractObject self, SPlayerController Instigator, int SubInteraction) =>
            ((STriggerInteraction)self).RefuseUse(Instigator, SubInteraction);

        [Uc("SBase.STriggerInteraction.s_Maintain_Use.PostInteract", Sha1 = "a5a52c36913854fa9c99c0747996f5203c4c7191")]
        [PortOf("uc:SBase.STriggerInteraction:s_Maintain_Use.PostInteract", Spec = "spec/objectives/switch-hack.md#rules")]
        public virtual void PostInteract(SInteractObject self, SPlayerController Instigator, int SubInteraction)
        {
            var entry = (STriggerInteraction)self;
            entry.HandleEventInfo();
            if (SubInteraction != K.SubHoldStarted)
            {
                ObjectiveNatives.ChangeState(entry, Name.None);
            }
        }
    }
}
