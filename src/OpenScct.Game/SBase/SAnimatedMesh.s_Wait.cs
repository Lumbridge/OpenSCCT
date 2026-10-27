using OpenScct.Core;
using OpenScct.Core.Engine;
using OpenScct.Core.Uc;
using OpenScct.Provenance;

namespace OpenScct.Game.SBase;

public partial class SAnimatedMesh
{
    /// <summary>Waiting (holding the last pose): a Trigger plays forwards, an UnTrigger backwards.</summary>
    [Uc("SBase.SAnimatedMesh.s_Wait")]
    public class s_Wait : UcActorState
    {
        public static readonly s_Wait Instance = new(UcMeta.s_Wait, null);

        protected s_Wait(UcStateInfo info, ActorState? super)
            : base(info, super)
        {
        }

        [Uc("SBase.SAnimatedMesh.s_Wait.BeginState", Sha1 = "f1050b22e1bff2a64f752a7a9fac489461d65678")]
        [PortOf("uc:SBase.SAnimatedMesh:s_Wait.BeginState", Spec = "spec/devices/animated-mesh.md#rules")]
        public override void BeginState(Actor self)
        {
            var mesh = (SAnimatedMesh)self;
            if (mesh.bAutoStart)
            {
                mesh.bAutoStart = false;
                mesh.GotoState(nameof(s_AnimateForward));
            }
        }

        /// <summary>Keeps the instigator for the chain's credit (the old one when none is given), then plays.</summary>
        [Uc("SBase.SAnimatedMesh.s_Wait.Trigger", Sha1 = "80b0f51f01e804fd6ff53282b7faff117fe8cba5")]
        [PortOf("uc:SBase.SAnimatedMesh:s_Wait.Trigger", Spec = "spec/devices/animated-mesh.md#rules")]
        public override void Trigger(Actor self, Actor? Other, Pawn? EventInstigator)
        {
            if (EventInstigator is not null)
            {
                self.Instigator = EventInstigator;
            }

            self.GotoState(nameof(s_AnimateForward));
        }

        [Uc("SBase.SAnimatedMesh.s_Wait.UnTrigger", Sha1 = "9d8146df1432bc3a037433d4d349e602477f108d")]
        [PortOf("uc:SBase.SAnimatedMesh:s_Wait.UnTrigger", Spec = "spec/devices/animated-mesh.md#rules")]
        public override void UnTrigger(Actor self, Actor? Other, Pawn? EventInstigator) => self.GotoState(nameof(s_AnimateBackward));
    }
}
